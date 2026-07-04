#nullable disable
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
namespace MouseMaster
{
    public class JitterEngine
    {
        private const double DriftNoise = 0.15;
        private const double DriftReversion = 0.01;
        private const double BaseAmp = 0.4;
        private const double ArousalRiseTauSec = 2.0;
        private const double ArousalDecayTauSec = 4.0;
        private const double ArousalJitterGain = 0.15;
        private const double FatigueOnsetRate = 0.00006;
        private const double FatigueDecayFast = 0.04;
        private const double FatigueDecaySlow = 0.003;
        private const double FatigueJitterGain = 0.2;
        private const double MotorDampingPerSec = 8.0;
        private const double MotorNoiseBase = 0.55;
        private const double MotorCrossCoupling = 0.18;
        private const double TremorGain = 0.22;
        private const double TremorPhaseNoise = 0.35;
        private const double SparseBaseRate = 0.45;
        private const double SparseAmpRate = 0.11;
        private const double MinDtSec = 0.001;
        private const double MaxDtSec = 3.0;
        private readonly FastRng _rnd;
        private readonly AppSettings _settings;
        private double _driftX, _driftY;
        private double _arousal;
        private double _fatigueShort, _fatigueLong;
        private long _lastTimestamp;
        private bool _firstCall = true;
        private double _targetCPS;
        private double _gaussSpare = double.NaN;
        private double _velX, _velY;
        private double _tremorPhaseX, _tremorPhaseY;
        private double _tremorFreqX, _tremorFreqY;
        private static double ResolveTargetCPS(AppSettings settings)
        {
            double cps = settings.IsManualInterval
                ? 1.0 / Math.Max((double)settings.IntervalSeconds, 0.001)
                : (double)settings.TargetCPS;

            if (!double.IsFinite(cps) || cps <= 0.0)
                return 1.0;

            return Math.Clamp(cps, 1.0, 500.0);
        }
        public JitterEngine(AppSettings settings, int? seed = null)
        {
            _settings = settings;
            _rnd = new FastRng(seed.HasValue ? (ulong)seed.Value ^ 0xC0FFEE42BABEFACEuL : (ulong)Environment.TickCount64);
            _targetCPS = ResolveTargetCPS(settings);
            _tremorPhaseX = _rnd.NextDouble() * Math.PI * 2.0;
            _tremorPhaseY = _rnd.NextDouble() * Math.PI * 2.0;
            _tremorFreqX = 7.0 + _rnd.NextDouble() * 5.0;
            _tremorFreqY = 7.0 + _rnd.NextDouble() * 5.0;
            _lastTimestamp = Stopwatch.GetTimestamp();
        }
        private double Gauss()
        {
            if (!double.IsNaN(_gaussSpare))
            {
                double s = _gaussSpare;
                _gaussSpare = double.NaN;
                if (Math.Abs(s) > 5.0) s = Math.Sign(s) * 5.0;
                return s;
            }
            double u, v, ss;
            do
            {
                u = _rnd.NextDouble() * 2.0 - 1.0;
                v = _rnd.NextDouble() * 2.0 - 1.0;
                ss = u * u + v * v;
            } while (ss >= 1.0 || ss == 0);
            double mul = Math.Sqrt(-2.0 * Math.Log(ss) / ss);
            _gaussSpare = Math.Clamp(v * mul, -5.0, 5.0);
            return Math.Clamp(u * mul, -5.0, 5.0);
        }
        public (int X, int Y) AdvanceAndGet()
        {
            if (!_settings.Jitter)
            {
                _lastTimestamp = Stopwatch.GetTimestamp();
                return (0, 0);
            }
            long now = Stopwatch.GetTimestamp();
            double dt = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            _lastTimestamp = now;
            if (_firstCall)
            {
                _firstCall = false;
                return (0, 0);
            }
            dt = Math.Clamp(dt, MinDtSec, MaxDtSec);
            double arousalTarget = Math.Clamp(_targetCPS / 20.0, 0.0, 1.0);
            double arousalTau = arousalTarget > _arousal ? ArousalRiseTauSec : ArousalDecayTauSec;
            _arousal += (arousalTarget - _arousal) * (1.0 - Math.Exp(-dt / arousalTau));
            _fatigueShort += (FatigueOnsetRate - _fatigueShort * FatigueDecayFast) * dt;
            _fatigueLong += (FatigueOnsetRate * 0.3 - _fatigueLong * FatigueDecaySlow) * dt;
            _fatigueShort = Math.Clamp(_fatigueShort, 0.0, 1.0);
            _fatigueLong = Math.Clamp(_fatigueLong, 0.0, 1.0);
            double fatigueTotal = _fatigueShort * 0.4 + _fatigueLong * 0.6;
            double scale = 1.0 + _arousal * ArousalJitterGain + fatigueTotal * FatigueJitterGain;
            double clickEquivalentDt = Math.Clamp(_targetCPS * dt, 0.05, 5.0);
            double driftReversion = 1.0 - Math.Exp(-DriftReversion * clickEquivalentDt);
            double driftNoise = DriftNoise * Math.Sqrt(clickEquivalentDt);
            _driftX += Gauss() * driftNoise - _driftX * driftReversion;
            _driftY += Gauss() * driftNoise - _driftY * driftReversion;
            double motorDecay = Math.Exp(-MotorDampingPerSec * dt);
            double motorSigma = MotorNoiseBase * Math.Sqrt(dt) * scale;
            double crossX = _velY * MotorCrossCoupling;
            double crossY = _velX * MotorCrossCoupling;
            _velX = _velX * motorDecay + crossX * (1.0 - motorDecay) + Gauss() * motorSigma;
            _velY = _velY * motorDecay + crossY * (1.0 - motorDecay) + Gauss() * motorSigma;
            int jitterX = Math.Max(0, _settings.JitterX);
            int jitterY = Math.Max(0, _settings.JitterY);
            double maxDriftX = Math.Max(1.0, jitterX * 1.5);
            double maxDriftY = Math.Max(1.0, jitterY * 1.5);
            _driftX = Math.Clamp(_driftX, -maxDriftX, maxDriftX);
            _driftY = Math.Clamp(_driftY, -maxDriftY, maxDriftY);
            _tremorPhaseX += 2.0 * Math.PI * _tremorFreqX * dt + Gauss() * TremorPhaseNoise * Math.Sqrt(dt);
            _tremorPhaseY += 2.0 * Math.PI * _tremorFreqY * dt + Gauss() * TremorPhaseNoise * Math.Sqrt(dt);
            double tremorX = Math.Sin(_tremorPhaseX) * jitterX * TremorGain * scale;
            double tremorY = Math.Sin(_tremorPhaseY) * jitterY * TremorGain * scale;

            double amp = Math.Sqrt(jitterX * jitterX + jitterY * jitterY);
            double eventRate = SparseBaseRate + SparseAmpRate * amp * scale;
            double pSilent = Math.Exp(-eventRate * clickEquivalentDt);
            if (_rnd.NextDouble() < pSilent)
                return (0, 0);
            int maxStepX = Math.Max(1, jitterX * 3);
            int maxStepY = Math.Max(1, jitterY * 3);
            int jitterStepX = (int)Math.Round(Gauss() * jitterX * BaseAmp * scale + _driftX + _velX + tremorX);
            int jitterStepY = (int)Math.Round(Gauss() * jitterY * BaseAmp * scale + _driftY + _velY + tremorY);
            return (Math.Clamp(jitterStepX, -maxStepX, maxStepX), Math.Clamp(jitterStepY, -maxStepY, maxStepY));
        }
        public void Reset()
        {
            _driftX = _driftY = 0;
            _velX = _velY = 0;
            _arousal = 0;
            _fatigueShort = _fatigueLong = 0;
            _tremorPhaseX = _rnd.NextDouble() * Math.PI * 2.0;
            _tremorPhaseY = _rnd.NextDouble() * Math.PI * 2.0;
            _tremorFreqX = 7.0 + _rnd.NextDouble() * 5.0;
            _tremorFreqY = 7.0 + _rnd.NextDouble() * 5.0;
            _firstCall = true;
            _lastTimestamp = Stopwatch.GetTimestamp();
        }
    }
}