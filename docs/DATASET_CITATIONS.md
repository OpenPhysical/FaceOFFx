# Dataset Citations

This document provides citations for all academic datasets used for validation testing in FaceOFFx. 

**Important Note**: These datasets are used exclusively for validation and quality assurance testing. No training is performed on these datasets. FaceOFFx uses only pre-trained models (RetinaFace and PFLD).

## SFHQ-T2I Dataset (Synthetic Faces High Quality - Text2Image)

**Purpose**: Validation of facial processing on high-quality synthetic faces, particularly for testing pre-cropped face handling.

**Citation**:
```
@misc{SFHQ-T2I,
  author = {SelfishGene},
  title = {Synthetic Faces High Quality - Text2Image Dataset},
  year = {2023},
  publisher = {GitHub},
  journal = {GitHub repository},
  howpublished = {\url{https://github.com/SelfishGene/SFHQ-T2I-dataset}}
}
```

**License**: Please refer to the original repository for licensing information.

**Special Considerations**: This dataset contains pre-cropped faces that may require relaxed cropping parameters during processing.

## WIDER FACE Dataset

**Purpose**: Validation on real-world face detection scenarios with varying scales, poses, and occlusions.

**Citation**:
```
@inproceedings{yang2016wider,
  title={WIDER FACE: A Face Detection Benchmark},
  author={Yang, Shuo and Luo, Ping and Loy, Chen Change and Tang, Xiaoou},
  booktitle={IEEE Conference on Computer Vision and Pattern Recognition (CVPR)},
  year={2016}
}
```

**Dataset URL**: http://shuoyang1213.me/WIDERFACE/

**License**: The WIDER FACE dataset is available for non-commercial research purposes only.

**Special Considerations**: This dataset contains many challenging images with multiple faces, occlusions, and extreme poses. Pre-filtering may be required for PIV compliance testing.

## ICAO Synthetic Dataset

**Purpose**: Validation of passport photo compliance and ICAO standard adherence.

**Citation**:
```
@article{borghi2018face,
  title={Face Verification from Depth using Privileged Information},
  author={Borghi, Guido and Fabbri, Matteo and Vezzani, Roberto and Calderara, Simone and Cucchiara, Rita},
  journal={British Machine Vision Conference (BMVC)},
  year={2018}
}
```

**Dataset URL**: https://miatbiolab.csr.unibo.it/icao-synthetic-dataset/

**License**: Please refer to the MIATBiolab website for licensing terms.

**Special Considerations**: This dataset is specifically designed for ICAO compliance testing and includes controlled lighting and pose variations.

## Usage Guidelines

1. **Download Instructions**: Users must download these datasets independently and place them in the `dataset/` directory (which is gitignored).

2. **Directory Structure**:
   ```
   dataset/
   ├── SFHQ-T2I/
   ├── wider-face/
   └── icao-synthetic/
   ```

3. **Processing Commands**:
   ```bash
   # Validate SFHQ-T2I with relaxed cropping
   dotnet run -- dataset validate ./dataset/SFHQ-T2I --standard piv --relaxed-crop
   
   # Process WIDER FACE with quality filtering
   dotnet run -- dataset validate ./dataset/wider-face --standard piv --filter-quality
   
   # Validate ICAO compliance
   dotnet run -- dataset validate ./dataset/icao-synthetic --standard icao
   ```

## Ethical Considerations

- These datasets are used solely for validation and quality assurance
- No biometric data is stored or transmitted
- Processing results are used only for technical validation
- Users must comply with each dataset's individual licensing terms

## Acknowledgments

We thank the creators and maintainers of these datasets for making them available to the research community. Their work enables rigorous validation of facial processing systems like FaceOFFx.