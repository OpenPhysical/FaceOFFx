using System;
using CoreJ2K.FaceOFFx.j2k.wavelet.analysis;

namespace CoreJ2K.FaceOFFx.j2k.entropy.encoder
{
    internal sealed class SynthesisPassGainAccumulator
    {
        internal readonly double[] TotalByPass = new double[32 * StdEntropyCoderOptions.NUM_PASSES];
        internal readonly double[] FaceByPass = new double[32 * StdEntropyCoderOptions.NUM_PASSES];
        private readonly SynthesisEnergyInfluence _influence;
        private SynthesisEnergyInfluence.BandEnergy _band;
        private int _offset, _scanWidth, _blockWidth, _blockHeight, _bandX, _bandY, _roiMagnitudeMask;
        private int _currentPass, _roiIntegerPassCount;
        private double _roiScale, _otherScale, _passTotal, _passFace, _total, _face;

        internal SynthesisPassGainAccumulator(SynthesisEnergyInfluence influence) =>
            _influence = influence ?? throw new ArgumentNullException(nameof(influence));

        internal void Reset(CBlkWTData block, int roiIntegerPassCount = 0)
        {
            _band = _influence.GetBand(block.sb);
            _offset = block.offset; _scanWidth = block.scanw;
            _blockWidth = block.w; _blockHeight = block.h;
            _bandX = block.ulx - block.sb.ulx; _bandY = block.uly - block.sb.uly;
            if (_bandX < 0 || _bandY < 0 || _blockWidth <= 0 || _blockHeight <= 0 ||
                _bandX + (long)_blockWidth > _band.Width || _bandY + (long)_blockHeight > _band.Height ||
                _scanWidth < _blockWidth)
                throw new InvalidOperationException("Shared attribution requires valid block geometry.");
            bool promoted = block.RoiWholeBandPromotion || block.RoiBlockAlignedPromotion;
            _roiMagnitudeMask = !promoted && block.RoiShift > 0 && block.nROIbp > 0
                ? ((1 << block.nROIbp) - 1) << (31 - block.nROIbp) : 0;
            _roiScale = promoted ? 1d / block.RoiPromotionScale : Math.Pow(2, -2 * block.RoiShift);
            _otherScale = promoted ? 1d / block.RoiPromotionScale : 1d;
            _passTotal = _passFace = _total = _face = 0;
            _currentPass = 0;
            _roiIntegerPassCount = Math.Max(0, roiIntegerPassCount);
        }

        internal void Add(int coefficient, int distortionDecrease, int coefficientIndex)
        {
            // Negative lookup changes retain their allocator meaning. Attribution credits positive gains only.
            if (distortionDecrease <= 0) return;
            bool roiCoefficient = (coefficient & _roiMagnitudeMask) != 0;
            // The Maxshift decoder keeps its fixed midpoint below the original ROI integer plane.
            // Background refinement and normally coded promoted bands retain their decoded benefit.
            if (roiCoefficient && _currentPass >= _roiIntegerPassCount) return;
            int relative = coefficientIndex - _offset;
            if (relative < 0 || relative % _scanWidth >= _blockWidth || relative / _scanWidth >= _blockHeight)
                throw new InvalidOperationException("Attributed coefficient lies outside its code-block.");
            int index = (_bandY + relative / _scanWidth) * _band.Width + _bandX + relative % _scanWidth;
            double scale = roiCoefficient ? _roiScale : _otherScale;
            double gain = distortionDecrease * scale;
            _passTotal += gain * _band.Total[index];
            _passFace += gain * _band.Face[index];
        }

        internal static int Record(SynthesisPassGainAccumulator accumulator, int coefficient, int decrease, int coefficientIndex)
        {
            accumulator?.Add(coefficient, decrease, coefficientIndex);
            return decrease;
        }

        internal void EndPass(int pass, double unweightedBitPlaneWeight)
        {
            double weight = unweightedBitPlaneWeight;
            _total += _passTotal * weight;
            _face += _passFace * weight;
            if (!double.IsFinite(_total) || !double.IsFinite(_face) || _total < 0 || _face < 0 || _face > _total * (1 + 1e-12))
                throw new InvalidOperationException("Shared pass gains exceed finite conserved model arithmetic.");
            TotalByPass[pass] = _total;
            FaceByPass[pass] = Math.Min(_face, _total);
            _passTotal = _passFace = 0;
            _currentPass = pass + 1;
        }
    }
}
