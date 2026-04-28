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
        private const double SparsityThreshold = 0.20;
        private const double BaseAmp = 0.4;
        private const double ArousalRiseTauSec = 2.0;
        private const double ArousalDecayTauSec = 4.0;
        private const double ArousalJitterGain = 0.15;
        private const double FatigueOnsetRate = 0.00006;
        private const double FatigueDecayFast = 0.04;
        private const double FatigueDecaySlow = 0.003;
        private const double FatigueJitterGain = 0.2;
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
        public JitterEngine(AppSettings settings, int? seed = null)
        {
            _settings = settings;
            _rnd = new FastRng(seed.HasValue ? (ulong)seed.Value ^ 0xC0FFEE42BABEFACEuL : (ulong)Environment.TickCount64);
            _targetCPS = settings.IsManualInterval ? 1000.0 / (double)settings.IntervalSeconds : (double)settings.TargetCPS;
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
                _firstCall = false;
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
            _driftX += Gauss() * DriftNoise - _driftX * DriftReversion;
            _driftY += Gauss() * DriftNoise - _driftY * DriftReversion;
            if (_rnd.NextDouble() < SparsityThreshold)
                return (0, 0);
            int jx = (int)Math.Round(Gauss() * _settings.JitterX * BaseAmp * scale + _driftX);
            int jy = (int)Math.Round(Gauss() * _settings.JitterY * BaseAmp * scale + _driftY);
            return (jx, jy);
        }
        public void Reset()
        {
            _driftX = _driftY = 0;
            _arousal = 0;
            _fatigueShort = _fatigueLong = 0;
            _firstCall = true;
            _lastTimestamp = Stopwatch.GetTimestamp();
        }
    }
}