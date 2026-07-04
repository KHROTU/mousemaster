#nullable disable
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
namespace MouseMaster
{
    public struct ClickSample
    {
        public double IntervalMs { get; set; }
        public double HoldMs { get; set; }
    }
    public class ClickerEngine
    {
        private const double OuTheta0 = 0.22;
        private const double OuTheta1 = 0.06;
        private const double OuTheta2 = 0.018;
        private const double OuTheta3 = 0.005;
        private const double OuWeight0 = 0.050;
        private const double OuWeight1 = 0.038;
        private const double OuWeight2 = 0.028;
        private const double OuWeight3 = 0.020;
        private const double TempoRange = 0.02;
        private const double TempoDriftHalfRange = 0.0006;
        private const double TempoJitter = 0.00015;
        private const double TempoClampMin = 0.92;
        private const double TempoClampMax = 1.08;
        private const double MomentumMin = 0.08;
        private const double MomentumRange = 0.12;
        private const double MomentumDropProb = 0.08;
        private const double MomentumDropFactor = 0.15;
        private const double BurstAmpSlow = 0.10;
        private const double BurstAmpFast = 0.04;
        private const double BurstNoiseFactor = 0.025;
        private const double BurstThreshold = 80.0;
        private const double FatigueOnsetFast = 0.00012;
        private const double FatigueDecayFast = 0.05;
        private const double FatigueOnsetSlow = 0.00004;
        private const double FatigueDecaySlow = 0.005;
        private const double FatigueMaxCoeffSlow = 0.030;
        private const double FatigueMaxCoeffFast = 0.008;
        private const double FatigueRecoveryFast = 2.0;
        private const double FatigueRecoverySlow = 0.5;
        private const double FatigueClickDivisor = 500.0;
        private const double DriftTargetPeriodAvg = 80.0;
        private const double DriftTargetMagnitude = 0.05;
        private const double DriftReversionRate = 0.015;
        private const double DriftTargetChangeProbPerClick = 1.0 / DriftTargetPeriodAvg;
        private const double MicroPauseMuFactor = 0.35;
        private const double MicroPauseSigma = 0.5;
        private const int MicroPauseIdxMin = 8;
        private const int MicroPauseIdxRange = 34;
        private const int WarmupMin = 6;
        private const int WarmupRange = 14;
        private const double WarmupProbTypeA = 0.12;
        private const double WarmupProbTypeB = 0.35;
        private const double WarmupSlowExtra = 0.25;
        private const double WarmupSlowExtraRange = 0.4;
        private const double WarmupFastExtra = 0.10;
        private const double WarmupFastExtraRange = 0.15;
        private const double WarmupGaussSlow = 0.15;
        private const double WarmupGaussFast = 0.06;
        private const double WarmupProgressDecay = 0.7;
        private const double RarePauseProb = 0.005;
        private const double RarePauseScale = 0.7;
        private const double AnalogNoiseUniform = 0.04;
        private const double AnalogNoiseGauss = 0.015;
        private const double IntervalFloor = 2.0;
        private const double SmoothFloorScale = 0.3;
        private const double HoldCenterMu = 0.30;
        private const double HoldCenterSigma = 0.04;
        private const double HoldLogSigma = 0.15;
        private const double HoldMotorRho = 0.05;
        private const double HoldOccasionalProb = 0.06;
        private const double HoldOccasionalSigma = 0.08;
        private const double HoldClampLow = 1.0;
        private const int RollingWindowSize = 50;
        private const int RollingWindowMinSamples = 10;
        private const double AdaptiveStrength = 0.2;
        private const double AdaptiveTargetCVRatio = 0.08;
        private const double AdaptiveClampMin = 0.5;
        private const double AdaptiveClampMax = 2.0;
        private const double CvBase = 0.045;
        private const double CvRandomStrengthGain = 0.09;
        private const double CvSlowTempoGain = 0.08;
        private const double CvFastTempoGain = 0.04;
        private const double CvAdaptationMix = 0.18;
        private const double HeavyTailMix = 0.32;
        private const double QuantizationDitherMs = 0.45;
        private const double QuantizationBlueMix = 0.68;
        private const double MinDtClicks = 0.5;
        private const double MaxDtClicks = 5.0;
        private const double AbsoluteMinInterval = 0.1;
        private readonly FastRng _rnd;
        private readonly AppSettings _settings;
        private readonly double _targetBaseMs;
        private readonly double _expectedCPS;
        private readonly double _factor;
        private double _gaussSpare = double.NaN;
        private double _personalTempo;
        private double _personalTempoDrift;
        private double _personalBurstBias;
        private double _personalHoldCenter;
        private readonly double[] _ouState;
        private readonly double[] _ouTheta;
        private readonly double[] _ouWeight;
        private double _prevInterval;
        private double _shortFatigue;
        private double _longFatigue;
        private double _burstPhase;
        private int _nextMicroPause;
        private double _driftTarget, _driftCurrent;
        private int _warmupLen;
        private int _clickCount;
        private long _lastTimestamp;
        private double _dynamicNoiseBoost = 1.0;
        private readonly double[] _rollingWindow = new double[RollingWindowSize];
        private int _rollingIdx;
        private int _rollingCount;
        private double _rollingSum;
        private double _rollingSumSq;
        private double _ditherPrevWhite;
        private double _ditherBlue;
        private static double ResolveTargetBaseMs(AppSettings settings)
        {
            double baseMs = settings.IsManualInterval
                ? (double)Math.Max(settings.IntervalSeconds, 0.001M) * 1000.0
                : 1000.0 / Math.Clamp((double)settings.TargetCPS, 1.0, 500.0);

            if (!double.IsFinite(baseMs) || baseMs <= 0.0)
                return IntervalFloor;

            return Math.Max(IntervalFloor, baseMs);
        }
        private static double ClampHoldMs(double holdMs, double intervalMs)
        {
            if (!double.IsFinite(intervalMs) || intervalMs <= 0.0)
                intervalMs = IntervalFloor;

            if (!double.IsFinite(holdMs) || holdMs <= 0.0)
                holdMs = intervalMs * HoldCenterMu;

            double maxHold = Math.Max(AbsoluteMinInterval, intervalMs * 0.85);
            double minHold = Math.Min(HoldClampLow, maxHold);
            return Math.Clamp(holdMs, minHold, maxHold);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SmoothMax(double x, double floor, double scale)
        {
            double scaled = (x - floor) / scale;
            if (scaled > 20.0) return x;
            if (scaled < -20.0) return floor;
            return floor + Math.Log(1.0 + Math.Exp(scaled)) * scale;
        }
        public ClickerEngine(AppSettings settings, int? seed = null)
        {
            _rnd = new FastRng(seed.HasValue ? (ulong)seed.Value ^ 0x9e3779b97f4a7c15uL : (ulong)Environment.TickCount64);
            _settings = settings;
            _targetBaseMs = ResolveTargetBaseMs(settings);
            _expectedCPS = 1000.0 / _targetBaseMs;
            _factor = settings.Randomize ? settings.RandomStrength / 10.0 : 0;
            _ouTheta = new[] { OuTheta0, OuTheta1, OuTheta2, OuTheta3 };
            _ouWeight = new[] { OuWeight0, OuWeight1, OuWeight2, OuWeight3 };
            _ouState = new double[4];
            RegenerateSessionProfile();
            _prevInterval = _targetBaseMs * _personalTempo;
            _nextMicroPause = MicroPauseIdxMin + _rnd.Next(MicroPauseIdxRange);
            _warmupLen = WarmupMin + _rnd.Next(WarmupRange);
            _lastTimestamp = Stopwatch.GetTimestamp();
        }
        private void RegenerateSessionProfile()
        {
            _personalTempo = 1.0 + SkewGauss(0.3) * TempoRange;
            _personalTempoDrift = (_rnd.NextDouble() - 0.5) * TempoDriftHalfRange * 2.0;
            _personalBurstBias = 0.5 + (_rnd.NextDouble() - 0.5) * 0.3;
            _personalHoldCenter = HoldCenterMu + Gauss() * HoldCenterSigma;
            _burstPhase = _rnd.NextDouble() * Math.PI * 2.0;
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
        private double SkewGauss(double alpha)
        {
            double u0 = Gauss(), v0 = Gauss();
            double delta = alpha / Math.Sqrt(1.0 + alpha * alpha);
            return delta * Math.Abs(u0) + Math.Sqrt(1.0 - delta * delta) * v0;
        }
        private double AnalogNoise()
        {
            return (_rnd.NextDouble() - 0.5) * AnalogNoiseUniform + Gauss() * AnalogNoiseGauss;
        }
        private double Laplace(double scale = 1.0)
        {
            double u = Math.Clamp(_rnd.NextDouble(), 1e-12, 1.0 - 1e-12);
            return scale * (u < 0.5 ? Math.Log(2.0 * u) : -Math.Log(2.0 * (1.0 - u)));
        }
        private double ComputeTargetCv()
        {
            double cps = _expectedCPS;
            double slowTerm = CvSlowTempoGain / (1.0 + cps / 7.0);
            double fastTerm = CvFastTempoGain * Math.Clamp((cps - 12.0) / 90.0, 0.0, 1.0);
            return CvBase + CvRandomStrengthGain * _factor + slowTerm + fastTerm;
        }
        private double QuantizationDither(double effectiveFactor)
        {
            double white = _rnd.NextDouble() - 0.5;
            _ditherBlue = QuantizationBlueMix * _ditherBlue + (1.0 - QuantizationBlueMix) * white;
            double highPass = white - _ditherPrevWhite;
            _ditherPrevWhite = white;
            return (0.55 * highPass + 0.45 * (_ditherBlue - white)) * QuantizationDitherMs * effectiveFactor;
        }
        public ClickSample Next()
        {
            long now = Stopwatch.GetTimestamp();
            if (!_settings.Randomize)
            {
                _lastTimestamp = now;
                _clickCount++;
                _prevInterval = _targetBaseMs;
                return new ClickSample { IntervalMs = _targetBaseMs, HoldMs = ClampHoldMs(_targetBaseMs * HoldCenterMu, _targetBaseMs) };
            }
            double dtRaw = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            _lastTimestamp = now;
            double dtClicks = dtRaw * _expectedCPS;
            if (dtClicks < MinDtClicks) dtClicks = 1.0;
            if (dtClicks > MaxDtClicks) dtClicks = MaxDtClicks;
            UpdateAdaptiveScaling();
            double z_motor = Gauss();
            double noiseSum = 0;
            double noiseScale = _targetBaseMs < 3 ? 0.6 : 1.0;
            double effectiveFactor = _factor * _dynamicNoiseBoost;
            for (int i = 0; i < _ouState.Length; i++)
            {
                double drift = _ouTheta[i] * (0 - _ouState[i]) * dtClicks;
                double vol = _ouWeight[i] * _targetBaseMs * effectiveFactor * noiseScale * Gauss() * Math.Sqrt(2.0 * _ouTheta[i] * dtClicks);
                _ouState[i] += drift + vol;
                noiseSum += _ouState[i];
            }
            _personalTempo += (_personalTempoDrift + Gauss() * TempoJitter) * dtClicks;
            _personalTempo = Math.Max(TempoClampMin, Math.Min(TempoClampMax, _personalTempo));
            double rawInterval = _targetBaseMs * _personalTempo + noiseSum;
            double momentum = MomentumMin + _rnd.NextDouble() * MomentumRange;
            if (_rnd.NextDouble() < MomentumDropProb) momentum *= MomentumDropFactor;
            double currentInterval = rawInterval * (1.0 - momentum) + _prevInterval * momentum;
            currentInterval += Math.Abs(SkewGauss(0.5)) * _targetBaseMs * 0.025 * effectiveFactor * (1.0 + 0.1 * z_motor);
            if (_targetBaseMs <= BurstThreshold)
            {
                double burstAmp = _targetBaseMs < 3 ? BurstAmpFast : BurstAmpSlow;
                _burstPhase += Math.PI + Gauss() * 0.8;
                double amp = _targetBaseMs * burstAmp * effectiveFactor;
                double shift = Math.Sin(_burstPhase) * amp * (0.5 + _personalBurstBias * 0.5);
                shift += Gauss() * _targetBaseMs * BurstNoiseFactor * effectiveFactor;
                currentInterval += shift;
            }
            _shortFatigue += (FatigueOnsetFast - _shortFatigue * FatigueDecayFast) * dtClicks;
            _longFatigue += (FatigueOnsetSlow - _longFatigue * FatigueDecaySlow) * dtClicks;
            _shortFatigue = Math.Clamp(_shortFatigue, 0.0, 1.0);
            _longFatigue = Math.Clamp(_longFatigue, 0.0, 1.0);
            double fatigueFastCoeff = _targetBaseMs < 3 ? FatigueMaxCoeffFast : FatigueMaxCoeffSlow;
            double fatigueFactor = 1.0 + (_shortFatigue * 0.35 + _longFatigue * 0.65) * fatigueFastCoeff * Math.Log(1.0 + _clickCount / FatigueClickDivisor);
            currentInterval *= fatigueFactor;
            if (_rnd.NextDouble() < DriftTargetChangeProbPerClick)
                _driftTarget = Gauss() * _targetBaseMs * DriftTargetMagnitude * effectiveFactor;
            _driftCurrent += (_driftTarget - _driftCurrent) * DriftReversionRate * dtClicks;
            currentInterval += _driftCurrent;
            _nextMicroPause--;
            if (_nextMicroPause <= 0)
            {
                double extraMs = Math.Exp(Gauss() * MicroPauseSigma + Math.Log(_targetBaseMs * MicroPauseMuFactor));
                currentInterval += extraMs;
                double pauseSec = extraMs / 1000.0;
                _shortFatigue *= Math.Exp(-pauseSec * FatigueRecoveryFast);
                _longFatigue *= Math.Exp(-pauseSec * FatigueRecoverySlow);
                _nextMicroPause = MicroPauseIdxMin + _rnd.Next(MicroPauseIdxRange);
            }
            if (_clickCount < _warmupLen)
            {
                bool gentleWarmup = _targetBaseMs < 5;
                double progress = (double)_clickCount / _warmupLen;
                double warmupExtra;
                double roll = _rnd.NextDouble();
                if (roll < WarmupProbTypeA)
                    warmupExtra = _targetBaseMs * 0.01 * (1.0 + _rnd.NextDouble() * 0.5);
                else if (roll < WarmupProbTypeB)
                    warmupExtra = _targetBaseMs * (gentleWarmup ? WarmupFastExtra + _rnd.NextDouble() * WarmupFastExtraRange : WarmupSlowExtra + _rnd.NextDouble() * WarmupSlowExtraRange);
                else
                    warmupExtra = Gauss() * _targetBaseMs * (gentleWarmup ? WarmupGaussFast : WarmupGaussSlow) * (1.0 - progress * WarmupProgressDecay);
                currentInterval += warmupExtra;
            }
            if (_rnd.NextDouble() < RarePauseProb * effectiveFactor)
                currentInterval += -Math.Log(1.0 - _rnd.NextDouble()) * _targetBaseMs * RarePauseScale;
            double targetCv = ComputeTargetCv();
            double logSigma = Math.Sqrt(Math.Log(1.0 + targetCv * targetCv));
            double shapedResidual = (1.0 - HeavyTailMix) * Gauss() + HeavyTailMix * Laplace();
            currentInterval *= Math.Exp(logSigma * shapedResidual);

            if (_rollingCount >= RollingWindowMinSamples)
            {
                double mean = _rollingSum / _rollingCount;
                double var = _rollingSumSq / _rollingCount - mean * mean;
                if (mean > 0.0 && var > 0.0)
                {
                    double actualCv = Math.Sqrt(var) / mean;
                    double cvError = (targetCv - actualCv) / Math.Max(targetCv, 1e-4);
                    currentInterval *= 1.0 + CvAdaptationMix * cvError;
                }
            }
            currentInterval += QuantizationDither(effectiveFactor);
            currentInterval = SmoothMax(currentInterval, IntervalFloor, SmoothFloorScale);
            currentInterval += AnalogNoise();
            currentInterval = Math.Max(AbsoluteMinInterval, currentInterval);
            double baseHold = currentInterval * _personalHoldCenter;
            double holdMs = baseHold * Math.Exp(HoldLogSigma * Gauss() + HoldMotorRho * z_motor);
            if (_rnd.NextDouble() < HoldOccasionalProb)
                holdMs *= Math.Exp(Gauss() * HoldOccasionalSigma);
            holdMs = ClampHoldMs(holdMs, currentInterval);
            if (_rollingCount == RollingWindowSize)
            {
                double old = _rollingWindow[_rollingIdx];
                _rollingSum -= old;
                _rollingSumSq -= old * old;
            }
            _rollingWindow[_rollingIdx] = currentInterval;
            _rollingSum += currentInterval;
            _rollingSumSq += currentInterval * currentInterval;
            _rollingIdx = (_rollingIdx + 1) % RollingWindowSize;
            if (_rollingCount < RollingWindowSize) _rollingCount++;
            _prevInterval = currentInterval;
            _clickCount++;
            return new ClickSample { IntervalMs = currentInterval, HoldMs = holdMs };
        }
        private void UpdateAdaptiveScaling()
        {
            if (_rollingCount < RollingWindowMinSamples) return;
            double mean = _rollingSum / _rollingCount;
            double actualVar = _rollingSumSq / _rollingCount - mean * mean;
            if (actualVar <= 0 || mean <= 0) return;
            double actualCV = Math.Sqrt(actualVar) / mean;
            double targetCV = Math.Max(ComputeTargetCv(), _factor * AdaptiveTargetCVRatio);
            double ratio = actualCV / targetCV;
            if (ratio < 0.5) 
            {
                _dynamicNoiseBoost = Math.Min(_dynamicNoiseBoost + 0.05, AdaptiveClampMax);
                RegenerateSessionProfile(); 
            }
            else if (ratio < 0.7)
                _dynamicNoiseBoost = Math.Min(_dynamicNoiseBoost + 0.02, AdaptiveClampMax);
            else if (ratio > 1.3)
                _dynamicNoiseBoost = Math.Max(_dynamicNoiseBoost - 0.01, AdaptiveClampMin);
            else
                _dynamicNoiseBoost += (1.0 - _dynamicNoiseBoost) * 0.1;
        }
    }
}