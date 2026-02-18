// File: Security.Utility/Authentication/WindowsHelloAuthenticator.cs

using System;
using System.Threading;
using System.Threading.Tasks;

#if WINDOWS
using Windows.Security.Credentials.UI;
#endif

namespace Security.Utility.Authentication
{
    /// <summary>
    /// Generic Windows Hello authenticator.
    ///
    /// Design goals:
    /// - Application-agnostic (no references to MWPV or any UI framework)
    /// - Small surface area (easy to reuse anywhere)
    /// - Async-only (Hello is inherently async)
    ///
    /// Notes:
    /// - Requires a Windows-targeting TFM for the Security.Utility project, e.g. net6.0-windows / net8.0-windows.
    /// - Requires access to WinRT API: Windows.Security.Credentials.UI.UserConsentVerifier.
    /// </summary>
    public static class WindowsHelloAuthenticator
    {
        public enum AvailabilityStatus
        {
            Unknown = 0,
            Available,
            NotConfigured,
            NotSupported,
            DisabledByPolicy,
            DeviceBusy,
            TemporarilyUnavailable
        }

        public enum VerifyStatus
        {
            Unknown = 0,
            Verified,
            Canceled,
            Rejected,
            NotConfigured,
            NotSupported,
            DisabledByPolicy,
            DeviceBusy,
            TemporarilyUnavailable,
            Error
        }

        public sealed class AvailabilityResult
        {
            public AvailabilityStatus Status { get; }
            public string? Detail { get; }

            public AvailabilityResult(AvailabilityStatus status, string? detail = null)
            {
                Status = status;
                Detail = detail;
            }

            public bool IsAvailable => Status == AvailabilityStatus.Available;
        }

        public sealed class VerifyResult
        {
            public VerifyStatus Status { get; }
            public string? Detail { get; }

            public VerifyResult(VerifyStatus status, string? detail = null)
            {
                Status = status;
                Detail = detail;
            }

            public bool IsVerified => Status == VerifyStatus.Verified;
        }

        /// <summary>
        /// Check whether Windows Hello verification is available on this machine for the current user.
        /// </summary>
        public static async Task<AvailabilityResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

#if WINDOWS
            try
            {
                var availability = await UserConsentVerifier.CheckAvailabilityAsync().AsTask(cancellationToken);

                return availability switch
                {
                    UserConsentVerifierAvailability.Available =>
                        new AvailabilityResult(AvailabilityStatus.Available),

                    UserConsentVerifierAvailability.DeviceNotPresent =>
                        new AvailabilityResult(AvailabilityStatus.NotSupported, "Hello device not present."),

                    UserConsentVerifierAvailability.NotConfiguredForUser =>
                        new AvailabilityResult(AvailabilityStatus.NotConfigured, "Hello not configured for current user."),

                    UserConsentVerifierAvailability.DisabledByPolicy =>
                        new AvailabilityResult(AvailabilityStatus.DisabledByPolicy, "Hello disabled by policy."),

                    UserConsentVerifierAvailability.DeviceBusy =>
                        new AvailabilityResult(AvailabilityStatus.DeviceBusy, "Hello device is busy."),

                    // Fallback for any new values that might appear
                    _ =>
                        new AvailabilityResult(AvailabilityStatus.TemporarilyUnavailable, availability.ToString())
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new AvailabilityResult(AvailabilityStatus.Unknown, ex.Message);
            }
#else
            return new AvailabilityResult(
                AvailabilityStatus.NotSupported,
                "Security.Utility is not built with a Windows target (missing WINDOWS compilation symbol / Windows TFM).");
#endif
        }

        /// <summary>
        /// Prompts Windows Hello verification for the current user.
        /// Caller provides a reason string (shown in the Hello prompt).
        /// </summary>
        public static async Task<VerifyResult> VerifyAsync(string reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Reason must be non-empty.", nameof(reason));

            cancellationToken.ThrowIfCancellationRequested();

#if WINDOWS
            try
            {
                // Optional: you can choose to pre-check availability in the caller.
                // We keep this method lean and just attempt verification.
                var result = await UserConsentVerifier.RequestVerificationAsync(reason).AsTask(cancellationToken);

                return result switch
                {
                    UserConsentVerificationResult.Verified =>
                        new VerifyResult(VerifyStatus.Verified),

                    UserConsentVerificationResult.Canceled =>
                        new VerifyResult(VerifyStatus.Canceled),

                    UserConsentVerificationResult.Rejected =>
                        new VerifyResult(VerifyStatus.Rejected),

                    UserConsentVerificationResult.DeviceNotPresent =>
                        new VerifyResult(VerifyStatus.NotSupported, "Hello device not present."),

                    UserConsentVerificationResult.NotConfiguredForUser =>
                        new VerifyResult(VerifyStatus.NotConfigured, "Hello not configured for current user."),

                    UserConsentVerificationResult.DisabledByPolicy =>
                        new VerifyResult(VerifyStatus.DisabledByPolicy, "Hello disabled by policy."),

                    UserConsentVerificationResult.DeviceBusy =>
                        new VerifyResult(VerifyStatus.DeviceBusy, "Hello device is busy."),

                    // Fallback for any new values that might appear
                    _ =>
                        new VerifyResult(VerifyStatus.TemporarilyUnavailable, result.ToString())
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new VerifyResult(VerifyStatus.Error, ex.Message);
            }
#else
            return new VerifyResult(
                VerifyStatus.NotSupported,
                "Security.Utility is not built with a Windows target (missing WINDOWS compilation symbol / Windows TFM).");
#endif
        }
    }
}
