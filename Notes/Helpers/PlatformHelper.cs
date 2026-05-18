namespace Notes.Helpers;

public static class PlatformHelper
{
  public static bool IsWindows()
  {
#if WINDOWS
        return true;
#else
    return false;
#endif
  }

  public static bool IsAndroid()
  {
#if ANDROID
    return true;
#else
        return false;
#endif
  }

  public static bool IsiOS()
  {
#if IOS
        return true;
#else
    return false;
#endif
  }

  public static bool IsMacCatalyst()
  {
#if MACCATALYST
        return true;
#else
    return false;
#endif
  }

  public static string GetPlatformName()
  {
#if WINDOWS
        return "Windows";
#elif ANDROID
    return "Android";
#elif IOS
        return "iOS";
#elif MACCATALYST
        return "macOS";
#else
        return "Unknown";
#endif
  }
}