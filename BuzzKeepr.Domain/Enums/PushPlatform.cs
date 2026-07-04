namespace BuzzKeepr.Domain.Enums;

// Which mobile OS issued a given Expo push token. Used for analytics + future platform-specific
// payload tweaks (e.g. iOS-only sound name, Android-only channel id).
public enum PushPlatform
{
    iOS,
    Android
}
