# Source color reference vectors

`lcms-matrix-vectors.json` contains synthetic RGB matrix-shaper profiles and 456 RGB8 reference samples per profile. Little CMS 2.19 generated both the profiles and reference conversions with relative-colorimetric rendering, black-point compensation disabled, and LUT optimization disabled. The profile timestamps were fixed before reference conversion, and source ICC SHA-256 digests are recorded in the fixture.

The v2 and v4 wide-gamut profiles use Adobe RGB primaries and gamma 2.19921875. The sRGB case tests numerical identity recognition. Alpha preservation is tested separately because the reference transform consumes RGB8. These fixtures describe color math; source acquisition evidence is supplied by the calling workflow.

Implementation references: [ICC.1:2022](https://www.color.org/specifications/ICC.1-2022-05.pdf), especially transform precedence, matrix/TRC processing, sampled curves, and corrected parametric functions; [ICC sRGB definition](https://registry.color.org/rgb-registry/files/sRGB.pdf), D50 matrix on page 4; [Little CMS](https://github.com/mm2/Little-CMS).
