using System;

// Simulate the Gabor filter creation
const float GaborSigma = 5.0f;
const float GaborLambda = 10.0f;
const float GaborGamma = 0.5f;
const int GaborKernelSize = 21;
var halfSize = GaborKernelSize / 2;

float maxKernel = 0f;
float minKernel = 0f;
float sumKernel = 0f;

for (var y = -halfSize; y <= halfSize; y++)
{
    for (var x = -halfSize; x <= halfSize; x++)
    {
        var xPrime = x;
        var yPrime = y;
        
        var exponent = -(xPrime * xPrime + GaborGamma * GaborGamma * yPrime * yPrime) / (2 * GaborSigma * GaborSigma);
        
        if (exponent < -20f)
        {
            continue;
        }
        
        var gaussian = MathF.Exp(exponent);
        var sinusoidal = MathF.Cos(2 * MathF.PI * xPrime / GaborLambda);
        
        var value = gaussian * sinusoidal;
        maxKernel = MathF.Max(maxKernel, value);
        minKernel = MathF.Min(minKernel, value);
        sumKernel += MathF.Abs(value);
    }
}

Console.WriteLine($"Gabor kernel range: [{minKernel:F6}, {maxKernel:F6}]");
Console.WriteLine($"Sum of absolute values: {sumKernel:F6}");

// Now simulate what happens with grayscale values 0-255
float maxResponse = 0f;
// Worst case: all pixels are 255, kernel values are all positive max
maxResponse = 255f * maxKernel * GaborKernelSize * GaborKernelSize;
Console.WriteLine($"Max possible response (before abs): {maxResponse:F2}");
Console.WriteLine($"Max response per pixel: {maxResponse / (GaborKernelSize * GaborKernelSize):F2}");
