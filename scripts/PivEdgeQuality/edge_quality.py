"""Source-referenced clothing-edge measurements in stored-image pixel units."""
from dataclasses import dataclass

import numpy as np


@dataclass(frozen=True)
class NecklineRegion:
    first_row: int = 495
    last_row: int = 631
    center_column: int = 240
    search_radius: int = 8


def blur(values, sigma):
    radius = int(np.ceil(3 * sigma))
    coordinates = np.arange(-radius, radius + 1)
    kernel = np.exp(-coordinates ** 2 / (2 * sigma ** 2))
    kernel /= kernel.sum()
    result = values.astype(np.float64)
    for axis in (0, 1):
        result = np.apply_along_axis(
            lambda row: np.convolve(np.pad(row, radius, mode="reflect"), kernel, "valid"), axis, result)
    return result


def erode(mask, radius):
    if radius == 0:
        return mask.copy()
    padded = np.pad(mask, radius, constant_values=False)
    return np.lib.stride_tricks.sliding_window_view(padded, (2 * radius + 1,) * 2).all(axis=(-2, -1))


def sample(values, x, y):
    x = np.clip(x, 0, values.shape[1] - 1.000001)
    y = np.clip(y, 0, values.shape[0] - 1.000001)
    xi, yi = np.floor(x).astype(int), np.floor(y).astype(int)
    xf, yf = x - xi, y - yi
    if values.ndim == 3:
        xf, yf = xf[..., None], yf[..., None]
    return ((1 - yf) * ((1 - xf) * values[yi, xi] + xf * values[yi, xi + 1])
            + yf * ((1 - xf) * values[yi + 1, xi] + xf * values[yi + 1, xi + 1]))


def crossings(row):
    positions = np.flatnonzero(row[:-1] * row[1:] <= 0)
    positions = positions[np.abs(row[positions + 1] - row[positions]) > 1e-9]
    return positions + (-row[positions] / (row[positions + 1] - row[positions]))


def locate_contours(rgb, region, anchors=None):
    opponent = blur(rgb[..., 0] - rgb[..., 1], .65)
    rows = np.arange(region.first_row, region.last_row + 1)
    output = []
    missing = []
    for side in range(2):
        positions = []
        for index, y in enumerate(rows):
            candidates = crossings(opponent[y])
            candidates = candidates[candidates < region.center_column] if side == 0 else candidates[candidates > region.center_column]
            if anchors is None:
                chosen = candidates.max() if side == 0 and candidates.size else candidates.min() if candidates.size else None
            else:
                distance = np.abs(candidates - anchors[side][index])
                candidates = candidates[distance <= region.search_radius]
                chosen = candidates[np.argmin(np.abs(candidates - anchors[side][index]))] if candidates.size else None
            missing.append(chosen is None)
            positions.append(float(chosen) if chosen is not None else float("nan"))
        output.append(np.array(positions))
    return rows, output, int(sum(missing))


def profile_geometry(rows, contour):
    slope = np.gradient(contour)
    normal_x = 1 / np.sqrt(1 + slope * slope)
    normal_y = -slope * normal_x
    offsets = np.linspace(-12, 12, 97)
    x = contour[:, None] + normal_x[:, None] * offsets
    y = rows[:, None] + normal_y[:, None] * offsets
    return offsets, x, y


def transition_width(profile, offsets):
    low, high = profile[:, :12].mean(axis=1), profile[:, -12:].mean(axis=1)
    amplitude = high - low
    normalized = (profile - low[:, None]) / np.where(np.abs(amplitude) > 1e-9, amplitude, 1)[:, None]
    positions = []
    for row in normalized:
        points = []
        for level in (.1, .9):
            hits = crossings(row - level)
            hits = np.interp(hits, np.arange(offsets.size), offsets)
            points.append(hits[np.argmin(np.abs(hits))] if hits.size else np.nan)
        positions.append(abs(points[1] - points[0]))
    return np.array(positions)


def make_reference(source, region=NecklineRegion()):
    source = np.array(source, dtype=np.float64, copy=True)
    if source.ndim != 3 or source.shape[2] != 3 or not np.isfinite(source).all():
        raise ValueError("Supply finite RGB source samples.")
    if not (0 <= region.first_row < region.last_row < source.shape[0] and 0 < region.center_column < source.shape[1]):
        raise ValueError("Neckline rows and center must fit the source.")
    if region.last_row - region.first_row < 12:
        raise ValueError("Neckline measurements require at least 13 consecutive source rows.")
    rows, contours, missing = locate_contours(source, region)
    if missing:
        raise ValueError("The source neckline needs a reviewed region or different color-boundary model.")
    for contour in contours:
        offsets, x, y = profile_geometry(rows, contour)
        if np.any(x < 0) or np.any(x >= source.shape[1] - 1) or np.any(y < 0) or np.any(y >= source.shape[0] - 1):
            raise ValueError("Normal edge profiles require complete source-pixel support.")
        original = sample(source, x, y)
        contrast = original[:, -12:].mean(axis=1) - original[:, :12].mean(axis=1)
        if np.any(np.linalg.norm(contrast, axis=1) <= 1e-9):
            raise ValueError("Every source edge profile requires positive RGB contrast.")
        if not np.isfinite(transition_width(original[..., 0] - original[..., 1], offsets)).all():
            raise ValueError("The source neckline needs complete 10%-90% transition support.")
    source_smooth = blur(source, .8)
    # This fixture's teal garment is separated from skin by the red-green opponent channel.
    clothing = (source_smooth[..., 1] - source_smooth[..., 0] > 20) & (np.arange(source.shape[0])[:, None] >= region.first_row - 30)
    interior = erode(clothing, 8)
    if interior.sum() < 100:
        raise ValueError("The source clothing region is too small for the color-detail measurement.")
    luminance = source_smooth @ np.array([.2126, .7152, .0722])
    gradient = np.stack(np.gradient(luminance), axis=-1)
    magnitude = np.linalg.norm(gradient, axis=-1)
    seams = interior & (magnitude >= max(.5, np.quantile(magnitude[interior], .90)))
    if seams.sum() < 10:
        raise ValueError("Source clothing has insufficient measured seam/fold gradient support.")
    reference = dict(source=source, source_smooth=source_smooth, region=region, rows=rows,
                     contours=contours, clothing=clothing, interior=interior, seams=seams,
                     source_gradient=gradient, gradient_magnitude=magnitude)
    for value in reference.values():
        if isinstance(value, np.ndarray):
            value.flags.writeable = False
    for value in contours:
        value.flags.writeable = False
    return reference


def measure(reference, candidate):
    candidate = np.asarray(candidate, dtype=np.float64)
    source = reference["source"]
    if candidate.shape != source.shape or not np.isfinite(candidate).all():
        raise ValueError("Candidate and source must have equal finite RGB geometry.")
    rows, contours, missing = locate_contours(candidate, reference["region"], reference["contours"])
    displacement = np.concatenate([actual - original for actual, original in zip(contours, reference["contours"])])
    jaggedness, width_error, halo, contrast_error, profile_error = [], [], [], [], []
    source_luma = source @ np.array([.2126, .7152, .0722])
    actual_luma = candidate @ np.array([.2126, .7152, .0722])
    for actual, original in zip(contours, reference["contours"]):
        residual = actual - original
        if np.isfinite(residual).all():
            # High-pass displacement removes gradual curve and position bias.
            jaggedness.extend(residual - blur(residual[:, None], 2)[:, 0])
        offsets, x, y = profile_geometry(rows, original)
        original_rgb, actual_rgb = sample(source, x, y), sample(candidate, x, y)
        original_contrast = original_rgb[:, -12:].mean(axis=1) - original_rgb[:, :12].mean(axis=1)
        actual_contrast = actual_rgb[:, -12:].mean(axis=1) - actual_rgb[:, :12].mean(axis=1)
        contrast_error.extend(np.linalg.norm(actual_contrast - original_contrast, axis=1)
                              / np.linalg.norm(original_contrast, axis=1))
        profile_error.extend((actual_rgb - original_rgb).ravel())
        original_opponent = original_rgb[..., 0] - original_rgb[..., 1]
        actual_opponent = actual_rgb[..., 0] - actual_rgb[..., 1]
        width_error.extend(transition_width(actual_opponent, offsets) - transition_width(original_opponent, offsets))
        original_profile, actual_profile = sample(source_luma, x, y), sample(actual_luma, x, y)
        lower, upper = original_profile.min(axis=1), original_profile.max(axis=1)
        excess = np.maximum(lower[:, None] - actual_profile, 0) + np.maximum(actual_profile - upper[:, None], 0)
        halo.extend(excess[:, np.abs(offsets) <= 6].ravel())
    smooth_actual = blur(candidate, .8)
    gradient = np.stack(np.gradient(smooth_actual @ np.array([.2126, .7152, .0722])), axis=-1)
    gradient_error = np.linalg.norm(gradient - reference["source_gradient"], axis=-1)
    source_chroma = np.stack((source[..., 0] - source[..., 1], source[..., 2] - source[..., 1]), axis=-1)
    actual_chroma = np.stack((candidate[..., 0] - candidate[..., 1], candidate[..., 2] - candidate[..., 1]), axis=-1)
    blotches = blur(actual_chroma - source_chroma, 3)
    def rms(values):
        values = np.asarray(values)
        return float(np.sqrt(np.mean(values * values))) if values.size and np.isfinite(values).all() else None
    return dict(necklineMissingRows=missing, necklinePositionRmsPixels=rms(displacement),
                necklinePositionP95Pixels=float(np.nanquantile(np.abs(displacement), .95)) if missing == 0 else None,
                necklineJaggednessRmsPixels=None if missing else rms(jaggedness), transitionWidthErrorRmsPixels=rms(width_error),
                transitionMissingSamples=int(np.count_nonzero(~np.isfinite(width_error))),
                edgeContrastRelativeErrorRms=rms(contrast_error), edgeNormalRgbProfileRmseSampleUnits=rms(profile_error),
                edgeHaloExcessRmsSampleUnits=rms(halo),
                seamGradientRmseSampleUnitsPerPixel=rms(gradient_error[reference["seams"]]),
                clothingChromaBlotchRmseSampleUnits=rms(blotches[reference["interior"]]),
                clothingInteriorPixels=int(reference["interior"].sum()), seamSupportPixels=int(reference["seams"].sum()),
                necklineSamples=int(displacement.size))
