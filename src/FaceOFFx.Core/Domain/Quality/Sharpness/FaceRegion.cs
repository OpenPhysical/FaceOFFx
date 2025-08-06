// MIT License
// 
// Copyright (c) 2025 FaceOFFx Contributors
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Identifies specific regions of a face for localized quality analysis
/// </summary>
[PublicAPI]
public enum FaceRegion
{
    /// <summary>
    /// The entire face region including all features
    /// </summary>
    FullFace = 0,
    
    /// <summary>
    /// The eye region including both eyes and eyebrows
    /// </summary>
    Eyes = 1,
    
    /// <summary>
    /// The nose region including bridge and nostrils
    /// </summary>
    Nose = 2,
    
    /// <summary>
    /// The mouth region including lips
    /// </summary>
    Mouth = 3,
    
    /// <summary>
    /// The forehead region above the eyebrows
    /// </summary>
    Forehead = 4,
    
    /// <summary>
    /// The left cheek region
    /// </summary>
    LeftCheek = 5,
    
    /// <summary>
    /// The right cheek region
    /// </summary>
    RightCheek = 6,
    
    /// <summary>
    /// The chin region below the mouth
    /// </summary>
    Chin = 7
}