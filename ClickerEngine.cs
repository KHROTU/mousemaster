#nullable disable
using System;
namespace MouseMaster
{
    public class ClickSample
    {
        public double IntervalMs { get; set; }
        public double HoldMs { get; set; }
        public int JitterX { get; set; }
        public int JitterY { get; set; }
    }
    public class ClickerEngine
    {
        private readonly Random _rnd;
        private readonly AppSettings _settings;
        private readonly double _targetBaseMs;
        private readonly double _factor;
        private double _gaussSpare = double.NaN;
        private double _personalTempo;
        private double _personalTempoDrift;
        private double _personalBurstBias;
        private double _personalHoldCenter;
        private double[] _ouState;
        private double[] _ouTheta = { 0.22, 0.06, 0.018, 0.005 };
        private double[] _ouWeight = { 0.050, 0.038, 0.028, 0.020 };
        private double _prevInterval;
        private double _fatigueLevel = 0;
        private int _nextRecovery;
        private bool _burstFlip = false;
        private int _nextMicroPause;
        private double _driftTarget = 0, _driftCurrent = 0;
        private int _warmupLen;
        private double _jitterDriftX = 0, _jitterDriftY = 0;
        private int _clickCount = 0;
        private double _momentumDecay = 0;
        public ClickerEngine(AppSettings settings, int? seed = null)
        {
            _rnd = seed.HasValue ? new Random(seed.Value) : new Random();
            _settings = settings;
            _targetBaseMs = settings.IsManualInterval
                ? (double)settings.IntervalSeconds * 1000.0
                : 1000.0 / (double)Math.Max(1, settings.TargetCPS);
            _factor = settings.Randomize ? settings.RandomStrength / 10.0 : 0;
            _personalTempo = 1.0 + SkewGauss(0.3) * 0.02;
            _personalTempoDrift = (_rnd.NextDouble() - 0.5) * 0.00008;
            _personalBurstBias = 0.5 + (_rnd.NextDouble() - 0.5) * 0.3;
            _personalHoldCenter = 0.30 + Gauss() * 0.04;
            _ouState = new double[4];
            _prevInterval = _targetBaseMs * _personalTempo;
            _nextRecovery = 120 + _rnd.Next(280);
            _nextMicroPause = 10 + _rnd.Next(40);
            _warmupLen = 6 + _rnd.Next(14);
            _momentumDecay = _rnd.NextDouble() * 0.04 + 0.02;
        }
        private double Gauss()
        {
            if (!double.IsNaN(_gaussSpare))
            {
                double s = _gaussSpare; _gaussSpare = double.NaN; return s;
            }
            double u, v, ss;
            do
            {
                u = _rnd.NextDouble() * 2.0 - 1.0;
                v = _rnd.NextDouble() * 2.0 - 1.0;
                ss = u * u + v * v;
            } while (ss >= 1.0 || ss == 0);
            double mul = Math.Sqrt(-2.0 * Math.Log(ss) / ss);
            _gaussSpare = v * mul;
            return u * mul;
        }
        private double SkewGauss(double alpha)
        {
            double u0 = Gauss(), v0 = Gauss();
            double delta = alpha / Math.Sqrt(1.0 + alpha * alpha);
            return delta * Math.Abs(u0) + Math.Sqrt(1.0 - delta * delta) * v0;
        }
        private double AnalogNoise()
        {
            return (_rnd.NextDouble() - 0.5) * 0.04 + Gauss() * 0.015;
        }
        public ClickSample Next()
        {
            double currentInterval;
            if (_settings.Randomize)
            {
                double noiseSum = 0;
                double noiseScale = _targetBaseMs < 3 ? 0.6 : 1.0;
                    for (int i = 0; i < _ouState.Length; i++)
                    {
                        _ouState[i] += _ouTheta[i] * (0 - _ouState[i])
                                    + _ouWeight[i] * _targetBaseMs * _factor * noiseScale
                                    * Gauss() * Math.Sqrt(2.0 * _ouTheta[i]);
                        noiseSum += _ouState[i];
                    }
                _personalTempo += _personalTempoDrift + Gauss() * 0.00003;
                _personalTempo = Math.Max(0.92, Math.Min(1.08, _personalTempo));
                double rawInterval = _targetBaseMs * _personalTempo + noiseSum;
                double momentum = 0.08 + _rnd.NextDouble() * 0.12;
                if (_rnd.NextDouble() < 0.08) momentum *= 0.15;
                currentInterval = rawInterval * (1.0 - momentum) + _prevInterval * momentum;
                currentInterval += Math.Abs(SkewGauss(0.5)) * _targetBaseMs * 0.025 * _factor;
                if (_targetBaseMs <= 80)
                {
                    double burstAmp = _targetBaseMs < 3 ? 0.04 : 0.10;
                    _burstFlip = !_burstFlip;
                    double amp = _targetBaseMs * burstAmp * _factor;
                    double shift = _burstFlip
                        ? -amp * _personalBurstBias
                        : amp * (1.0 - _personalBurstBias);
                    shift += Gauss() * _targetBaseMs * 0.025 * _factor;
                    currentInterval += shift;
                    if (_rnd.NextDouble() < 0.03) _burstFlip = !_burstFlip;
                }
                _fatigueLevel += (0.00012 + _rnd.NextDouble() * 0.00008) * _factor;
                _nextRecovery--;
                if (_nextRecovery <= 0)
                {
                    _fatigueLevel *= 0.5 + _rnd.NextDouble() * 0.3;
                    _nextRecovery = 80 + _rnd.Next(320);
                }
                double fatigueCoeff = _targetBaseMs < 3 ? 0.008 : 0.030;
                currentInterval += Math.Log(1.0 + _fatigueLevel * _clickCount) * _targetBaseMs * fatigueCoeff;
                if (_clickCount > 0 && _clickCount % (40 + _rnd.Next(120)) == 0)
                    _driftTarget = Gauss() * _targetBaseMs * 0.05 * _factor;
                _driftCurrent += (_driftTarget - _driftCurrent) * 0.015;
                currentInterval += _driftCurrent;
                _nextMicroPause--;
                if (_nextMicroPause <= 0)
                {
                    double extraMs = Math.Exp(Gauss() * 0.5 + Math.Log(_targetBaseMs * 0.35));
                    currentInterval += extraMs;
                    _nextMicroPause = 8 + _rnd.Next(50);
                }
                bool gentleWarmup = _targetBaseMs < 5;
                if (_clickCount < _warmupLen)
                {
                    double progress = (double)_clickCount / _warmupLen;
                    double warmupExtra;
                    double roll = _rnd.NextDouble();
                    if (roll < 0.12)
                        warmupExtra = _targetBaseMs * 0.01 * (1.0 + _rnd.NextDouble() * 0.5);
                    else if (roll < 0.35)
                        warmupExtra = _targetBaseMs * (gentleWarmup ? 0.10 + _rnd.NextDouble() * 0.15 : 0.25 + _rnd.NextDouble() * 0.4);
                    else
                        warmupExtra = Math.Abs(Gauss()) * _targetBaseMs * (gentleWarmup ? 0.06 : 0.15) * (1.0 - progress * 0.7);
                    currentInterval += warmupExtra;
                }
                if (_rnd.NextDouble() < 0.005 * _factor)
                    currentInterval += -Math.Log(1.0 - _rnd.NextDouble()) * _targetBaseMs * 0.7;
            }
            else
            {
                currentInterval = _targetBaseMs;
            }
            if (currentInterval < 2.0) currentInterval = 2.0 + _rnd.NextDouble() * 0.5;
            currentInterval += AnalogNoise();
            double holdRatio = Gauss() * 0.06 + _personalHoldCenter;
            holdRatio = Math.Max(0.12, Math.Min(0.58, holdRatio));
            if (currentInterval < _targetBaseMs * 0.85) holdRatio *= 0.88;
            if (_rnd.NextDouble() < 0.06) holdRatio += Gauss() * 0.08;
            double holdTime = currentInterval * holdRatio;
            if (holdTime < 1.0) holdTime = 1.0 + _rnd.NextDouble() * 0.5;
            holdTime += AnalogNoise() * 0.3;
            int jx = 0, jy = 0;
            if (_settings.Jitter)
            {
                _jitterDriftX += Gauss() * 0.15 - _jitterDriftX * 0.01;
                _jitterDriftY += Gauss() * 0.15 - _jitterDriftY * 0.01;
                if (_rnd.NextDouble() > 0.20)
                {
                    jx = (int)Math.Round(Gauss() * _settings.JitterX * 0.4 + _jitterDriftX);
                    jy = (int)Math.Round(Gauss() * _settings.JitterY * 0.4 + _jitterDriftY);
                }
            }
            _prevInterval = currentInterval;
            _clickCount++;
            return new ClickSample { IntervalMs = currentInterval, HoldMs = holdTime, JitterX = jx, JitterY = jy };
        }
    }
}