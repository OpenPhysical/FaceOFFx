"""Decoded RGB response measurements. Composed responses propose maps for real decoding."""
import math

import numpy as np


def _rgb(values):
    values = np.asarray(values, dtype=np.float64)
    if values.ndim != 3 or values.shape[2] != 3 or not np.isfinite(values).all():
        raise ValueError("Supply finite H x W x 3 RGB samples.")
    if np.any(values < 0) or np.any(values > 255):
        raise ValueError("Decoded RGB samples must be in the eight-bit range.")
    return values


def _mask(mask, shape):
    mask = np.asarray(mask)
    if mask.dtype != np.bool_ or mask.shape != shape or not np.any(mask):
        raise ValueError("Supply a nonempty boolean mask on the frozen RGB grid.")
    return mask


def measure_response(source, baseline, candidate, masks):
    source, baseline, candidate = map(_rgb, (source, baseline, candidate))
    if source.shape != baseline.shape or source.shape != candidate.shape:
        raise ValueError("Source and decoded images must have equal geometry.")
    delta = candidate - baseline
    before, after = (baseline-source)**2, (candidate-source)**2
    regions = {}
    for name, values in masks.items():
        mask = _mask(values, source.shape[:2])
        count = int(mask.sum()) * 3
        old_sse, new_sse = float(before[mask].sum()), float(after[mask].sum())
        def psnr(sse):
            return 10*math.log10(255**2/(sse/count)) if sse else None
        regions[name] = dict(rgbSamples=count, baselineSse=old_sse, candidateSse=new_sse,
                             deltaSse=new_sse-old_sse, baselineMse=old_sse/count,
                             candidateMse=new_sse/count, baselinePsnrDb=psnr(old_sse),
                             candidatePsnrDb=psnr(new_sse),
                             changedPixels=int(np.any(delta != 0, axis=2)[mask].sum()))
    delta = delta.copy()
    delta.flags.writeable = False
    return dict(regions=regions, deltaRgb=delta)


def predict_composed_response(baseline, deltas):
    """Clipped linear proposal, requiring an independent decode before acceptance."""
    baseline = _rgb(baseline)
    prediction = baseline.copy()
    for delta in deltas:
        delta = np.asarray(delta, dtype=np.float64)
        if delta.shape != baseline.shape or not np.isfinite(delta).all() or np.any(np.abs(delta) > 255):
            raise ValueError("Responses must be finite eight-bit RGB differences on the frozen grid.")
        prediction += delta
    prediction = np.clip(prediction, 0, 255)
    prediction.flags.writeable = False
    return prediction


def response_interference(source, baseline, deltas, masks):
    """Expose cross terms omitted by summing individual source-error changes."""
    source, baseline = map(_rgb, (source, baseline))
    if source.shape != baseline.shape:
        raise ValueError("Source and baseline must have equal geometry.")
    deltas = [np.asarray(delta, dtype=np.float64) for delta in deltas]
    predicted = predict_composed_response(baseline, deltas)
    total = np.zeros_like(baseline)
    squared_terms = np.zeros_like(baseline)
    individual_changes = np.zeros_like(baseline)
    residual = baseline-source
    for delta in deltas:
        total += delta
        squared_terms += delta**2
        individual_changes += 2*residual*delta + delta**2
    unclipped = baseline + total
    output = {}
    for name, values in masks.items():
        mask = _mask(values, source.shape[:2])
        base_sse = float((residual[mask]**2).sum())
        independent_delta = float(individual_changes[mask].sum())
        cross = float((total[mask]**2-squared_terms[mask]).sum())
        raw_sse = float(((unclipped[mask]-source[mask])**2).sum())
        output[name] = dict(baselineSse=base_sse, sumIndividualDeltaSse=independent_delta,
                            crossTermSse=cross, unclippedLinearSse=raw_sse,
                            clippedLinearSse=float(((predicted[mask]-source[mask])**2).sum()),
                            clippedSamples=int(np.count_nonzero((unclipped[mask] < 0) | (unclipped[mask] > 255))),
                            algebraClosureResidual=raw_sse-(base_sse+independent_delta+cross),
                            acceptanceScope="Proposal only; independent full-map decode required")
    return output
