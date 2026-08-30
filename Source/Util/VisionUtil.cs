using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalk.Util;

public static class VisionUtil
{
    public const int DefaultMaxDimension = 1024;
    public const int DefaultJpgQuality = 75;

    private static readonly System.Reflection.FieldInfo ScreenshotModeActiveField =
        AccessTools.Field(typeof(ScreenshotModeHandler), "active");

    /// <summary>
    /// Enables or disables RimWorld HUD screenshot mode and RimTalk overlay suppression.
    /// </summary>
    public static void SetScreenshotMode(bool active)
    {
        if (Find.UIRoot?.screenshotMode != null && ScreenshotModeActiveField != null)
        {
            ScreenshotModeActiveField.SetValue(Find.UIRoot.screenshotMode, active);
        }
        UI.Overlay.SuppressForScreenshot = active;
    }

    /// <summary>
    /// Clears any active Interaction Bubbles on screen.
    /// </summary>
    public static void ClearBubbles()
    {
        try
        {
            var bubblerType = AccessTools.TypeByName("Bubbles.Core.Bubbler");
            if (bubblerType != null)
            {
                var clearMethod = AccessTools.Method(bubblerType, "Clear");
                clearMethod?.Invoke(null, null);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to clear bubbles: {ex.Message}");
        }
    }

    /// <summary>
    /// Draws item and resource stack labels directly on GUI during clean vision screenshots.
    /// </summary>
    public static void DrawThingOverlays()
    {
        try
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            CellRect viewRect = Find.CameraDriver != null ? Find.CameraDriver.CurrentViewRect : CellRect.Empty;
            var haulables = map.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableAlways);
            if (haulables != null)
            {
                for (int i = 0; i < haulables.Count; i++)
                {
                    var thing = haulables[i];
                    if (thing == null || !thing.Spawned || thing.Destroyed) continue;
                    if (!viewRect.IsEmpty && !viewRect.Contains(thing.Position)) continue;
                    if (map.fogGrid != null && map.fogGrid.IsFogged(thing.Position)) continue;

                    string label = thing.LabelShortCap;
                    if (thing.stackCount > 1 && !label.Contains(thing.stackCount.ToString()))
                    {
                        label = $"{label} x{thing.stackCount}";
                    }
                    GenMapUI.DrawThingLabel(thing, label);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to draw ThingOverlays: {ex.Message}");
        }
    }


    /// <summary>
    /// Asynchronously captures a clean screenshot of the current viewport at WaitForEndOfFrame with HUD suppressed.
    /// </summary>
    public static void CaptureScreenAsync(Action<string> onComplete, int maxDimension = DefaultMaxDimension, int quality = DefaultJpgQuality)
    {
        CoroutineRunner.Instance.StartCoroutine(CaptureScreenRoutine(onComplete, maxDimension, quality));
    }

    private static IEnumerator CaptureScreenRoutine(Action<string> onComplete, int maxDimension, int quality)
    {
        // 1. Close open float menus
        if (Find.WindowStack != null)
        {
            var floatMenus = Find.WindowStack.Windows.Where(w => w is FloatMenu).ToList();
            foreach (var fm in floatMenus)
            {
                fm.Close(false);
            }
        }

        // 2. Suppress HUD and bubbles for clean capture
        ClearBubbles();
        SetScreenshotMode(true);

        yield return new WaitForEndOfFrame();

        string base64 = null;
        try
        {
            Texture2D rawScreen = ScreenCapture.CaptureScreenshotAsTexture();
            if (rawScreen != null)
            {
                Texture2D scaled = ScaleTexture(rawScreen, maxDimension);
                try
                {
                    byte[] jpgBytes = scaled.EncodeToJPG(quality);
                    base64 = Convert.ToBase64String(jpgBytes);
                }
                finally
                {
                    UnityEngine.Object.Destroy(scaled);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to capture screen screenshot: {ex.Message}");
        }
        finally
        {
            SetScreenshotMode(false);
        }

        onComplete?.Invoke(base64);
    }

    /// <summary>
    /// Scales texture down to maxDimension if it exceeds it, disposing the original texture if scaled.
    /// </summary>
    public static Texture2D ScaleTexture(Texture2D source, int maxDimension)
    {
        if (source == null) return null;
        if (source.width <= maxDimension && source.height <= maxDimension) return source;

        float scale = Mathf.Min((float)maxDimension / source.width, (float)maxDimension / source.height);
        int newWidth = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
        int newHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Graphics.Blit(source, rt);

        Texture2D scaledTex = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
        scaledTex.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        scaledTex.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.Destroy(source);

        return scaledTex;
    }

    /// <summary>
    /// Synchronously captures the current screen and returns a Base64 JPG string.
    /// </summary>
    public static string CaptureCurrentScreenBase64(int maxDimension = DefaultMaxDimension, int quality = DefaultJpgQuality)
    {
        try
        {
            Texture2D rawScreen = ScreenCapture.CaptureScreenshotAsTexture();
            if (rawScreen == null) return null;

            Texture2D scaled = ScaleTexture(rawScreen, maxDimension);
            try
            {
                byte[] jpgBytes = scaled.EncodeToJPG(quality);
                return Convert.ToBase64String(jpgBytes);
            }
            finally
            {
                UnityEngine.Object.Destroy(scaled);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to capture current screen Base64: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copies an image to the native OS system clipboard (macOS / Windows / Linux) as an actual image bitmap.
    /// </summary>
    public static bool CopyImageToClipboard(Texture2D texture, string base64Fallback = null)
    {
        if (texture == null && string.IsNullOrEmpty(base64Fallback)) return false;

        try
        {
            byte[] pngBytes = null;
            if (texture != null)
            {
                pngBytes = texture.EncodeToPNG();
            }
            else if (!string.IsNullOrEmpty(base64Fallback))
            {
                pngBytes = Convert.FromBase64String(base64Fallback);
            }

            if (pngBytes == null || pngBytes.Length == 0) return false;

            string tempFile = Path.Combine(Path.GetTempPath(), $"rimtalk_clip_{Guid.NewGuid():N}.png");
            File.WriteAllBytes(tempFile, pngBytes);

            if (Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e 'set the clipboard to (read (POSIX file \"{tempFile}\") as «class PNGf»)'",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(1000);
            }
            else if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Clipboard]::SetImage([System.Drawing.Image]::FromFile('{tempFile}'))\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(2000);
            }
            else // Linux
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xclip",
                    Arguments = $"-selection clipboard -target image/png -i \"{tempFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(1000);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to copy image to native clipboard: {ex.Message}");
            return false;
        }
    }
}

public class CoroutineRunner : MonoBehaviour
{
    private static CoroutineRunner _instance;

    public static CoroutineRunner Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("RimTalk_CoroutineRunner");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<CoroutineRunner>();
            }
            return _instance;
        }
    }
}
