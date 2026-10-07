#nullable enable

using System;
using CoreJ2K.FaceOFFx.j2k.entropy.encoder;

namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>Encoded bytes and measured codec diagnostics. Biometric qualification requires acquisition, regional and record validation.</summary>
    internal sealed class EncoderResult
    {
        public byte[] Data { get; }
        public int CodestreamBytes { get; }
        public int ContainerBytes { get; }
        public long RoiPixelCount { get; }
        /// <summary>Measured Maxshift protected-phase payload, excluding packet and container headers.</summary>
        public long? ProtectedPayloadBytes { get; }
        public string ProtectedPayloadAccounting { get; }
        /// <summary>Exact packet and allocation counters for the byte-budgeted path; otherwise null.</summary>
        public BudgetAllocationDiagnostics? AllocationDiagnostics { get; }
        /// <summary>Emitted payload and complete-output accounting when requested; otherwise null.</summary>
        public PayloadTelemetry? PayloadTelemetry { get; }
        /// <summary>Versioned operational regional payload attribution when requested; otherwise null.</summary>
        public SharedPayloadAttribution? SharedPayloadAttribution { get; }

        public EncoderResult(byte[] data, int codestreamBytes, int containerBytes, long roiPixelCount,
            long? protectedPayloadBytes, string protectedPayloadAccounting,
            BudgetAllocationDiagnostics? allocationDiagnostics = null, PayloadTelemetry? payloadTelemetry = null,
            SharedPayloadAttribution? sharedPayloadAttribution = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (codestreamBytes < 0 || containerBytes < 0 || (long)codestreamBytes + containerBytes != data.Length)
                throw new ArgumentException("Codestream and container lengths must equal the complete output length.");
            if (roiPixelCount < 0) throw new ArgumentOutOfRangeException(nameof(roiPixelCount));
            if (protectedPayloadBytes is < 0) throw new ArgumentOutOfRangeException(nameof(protectedPayloadBytes));
            Data = data;
            CodestreamBytes = codestreamBytes;
            ContainerBytes = containerBytes;
            RoiPixelCount = roiPixelCount;
            ProtectedPayloadBytes = protectedPayloadBytes;
            ProtectedPayloadAccounting = protectedPayloadAccounting ?? throw new ArgumentNullException(nameof(protectedPayloadAccounting));
            AllocationDiagnostics = allocationDiagnostics;
            PayloadTelemetry = payloadTelemetry;
            if (sharedPayloadAttribution != null && (payloadTelemetry == null ||
                sharedPayloadAttribution.PacketBodyBytes != payloadTelemetry.PacketBodyBytes ||
                sharedPayloadAttribution.RoiPixelCount != roiPixelCount))
                throw new ArgumentException("Shared attribution must reconcile with spatial ROI pixels and emitted payload telemetry.", nameof(sharedPayloadAttribution));
            SharedPayloadAttribution = sharedPayloadAttribution;
        }
    }
}
