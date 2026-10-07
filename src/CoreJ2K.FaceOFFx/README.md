# FaceOFFx encoder component

This internal, net8.0-only assembly contains the encoder closure from the tested CoreJ2K working tree. FaceOFFx ships it in its single library package.

`BalancedEncoder.Encode` accepts caller-owned RGB24 pixels, a complete JP2 byte cap and an optional immutable face region. Its recipe is fixed: one tile, one quality layer, irreversible ICT and 9/7, five decompositions, 64×64 code blocks, start-level 4 Maxshift priority, and luma allocation utility 1.25. The allocator uses the original raw rate-distortion objective.

Committed packet-body telemetry and `synthesis-energy-decoder-effective-pass-v3` attribution are retained. Attribution measures coding benefit under the versioned coefficient-diagonal synthesis-energy convention. The returned face credit supports application review of the fixed mask and emitted payload.

The source boundary includes encoder dependencies and the finite 9/7 synthesis filter used by attribution. Decoder pipelines, file ROI parsing, allocation quotas and research hooks were removed. All codec types are internal; Infrastructure and the encoder test assembly use explicit friend access.

`provenance.json` records original source hashes, upstream revision, the tested working-tree context and final vendored hashes. `LICENSE` and `COPYRIGHT-JJ2000-5.1` retain the original notices. Update this component as one reviewed snapshot, then run the golden medical byte-parity test, payload reconciliation tests and independent decoding before changing the recipe.
