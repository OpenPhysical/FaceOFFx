# Balanced encoder control

These immutable fixtures preserve the approved source-watermarked medical crop and 68-landmark face mask used during the JPEG 2000 quality study. The original supplied source is `datasets/samples/cardholders/source_watermarked/medical/german-nurse-male.png`. The crop is 480×640; its fixed coding mask contains 60,318 pixels.

`medical-balanced.jp2` is the tested 11,771-byte balanced control: start4, b64, five decompositions, ICT/9-7, one layer, luma utility 1.25, cap 11,820. Its SHA-256 is `0bcf6af3c6be455de0343981ed9cdd8fb4cdfd0cce7f4c99c877a8ff95e14a52`. The V3 operational face credit is 3,383 bytes. Source acquisition, optical geometry and complete signed PIV-record qualification remain application review requirements.

The golden test compares every emitted byte. Independent Pillow/OpenJPEG 2.5.4 decoding yields RGB 480×640 with decoded sample SHA-256 `161aa6b722ae0fd0ccd8cf3d3833a94fe8e2bcdd0058c524f6187f24d21db129`.
