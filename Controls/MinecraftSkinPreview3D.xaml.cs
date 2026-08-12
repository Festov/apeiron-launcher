using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Apeiron.Services;

namespace Apeiron.Controls;

public partial class MinecraftSkinPreview3D : UserControl
{
    private const string VirtualHost = "apeiron.assets";
    private const string BundleFileName = "skinview3d.bundle.js";

    private bool _ready;
    private bool _initializing;
    private int _skinGeneration;
    private string? _pendingBase64;
    private bool _pendingSlim;
    private string _dragHint = "Drag to rotate";
    private string _backgroundHex = "#07090D";
    private BitmapSource? _lastSkin;
    private bool _lastSlim;
    private int _lastW;
    private int _lastH;
    private Task? _initTask;
    private string? _minecraftDir;

    public MinecraftSkinPreview3D()
    {
        InitializeComponent();
        Loaded += (_, _) => _ = EnsureBrowserAsync();
        SizeChanged += (_, _) =>
        {
            var w = (int)ActualWidth;
            var h = (int)ActualHeight;
            if (_lastSkin == null || w < 40 || h < 40)
                return;
            if (Math.Abs(w - _lastW) < 12 && Math.Abs(h - _lastH) < 12)
                return;
            _lastW = w;
            _lastH = h;
            _ = ResizeViewerAsync(w, h);
        };
    }

    public void SetMinecraftDir(string? minecraftDir) => _minecraftDir = minecraftDir;

    public void SetDragHint(string text)
    {
        _dragHint = text;
        DragHint.Text = text;
    }

    public void SetBackground(string? hex)
    {
        _backgroundHex = SettingsService.NormalizeHexColor(hex, "#07090D");
        try
        {
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_backgroundHex)!);
        }
        catch
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(7, 9, 13));
        }

        if (Browser != null)
            Browser.DefaultBackgroundColor = HexToDrawingColor(_backgroundHex);

        if (_ready)
            _ = ApplyBackgroundAsync();
    }

    public void SetSkin(BitmapSource? skin, bool slim)
    {
        _lastSkin = skin;
        _lastSlim = slim;

        if (skin == null)
        {
            StatusText.Text = "";
            _pendingBase64 = null;
            return;
        }

        byte[] png;
        try
        {
            png = EncodePng(skin);
        }
        catch
        {
            StatusText.Text = "Failed to read skin";
            return;
        }

        var base64 = Convert.ToBase64String(png);
        _pendingBase64 = base64;
        _pendingSlim = slim;
        var generation = ++_skinGeneration;
        StatusText.Text = "";
        _ = ApplySkinAsync(base64, slim, generation);
    }

    private async Task EnsureBrowserAsync()
    {
        if (_ready)
            return;
        if (_initTask != null)
        {
            await _initTask;
            return;
        }

        _initTask = InitializeBrowserCoreAsync();
        await _initTask;
    }

    private async Task InitializeBrowserCoreAsync()
    {
        if (_ready || _initializing)
            return;

        _initializing = true;
        try
        {
            StatusText.Text = "...";
            var env = await WebView2Helper.CreateEnvironmentAsync(_minecraftDir);
            await Browser.EnsureCoreWebView2Async(env);
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.DefaultBackgroundColor = HexToDrawingColor(_backgroundHex);

            var assetDir = EnsureLocalSkinviewBundle(_minecraftDir);
            Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                assetDir,
                CoreWebView2HostResourceAccessKind.Allow);

            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNav(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                Browser.CoreWebView2.NavigationCompleted -= OnNav;
                loaded.TrySetResult(e.IsSuccess);
            }

            Browser.CoreWebView2.NavigationCompleted += OnNav;
            Browser.NavigateToString(BuildShellHtml(_backgroundHex));
            if (!await loaded.Task.ConfigureAwait(true))
                throw new InvalidOperationException("WebView2 navigation failed");

            _ready = true;
            StatusText.Text = "";
            await ApplyBackgroundAsync();

            if (_pendingBase64 != null)
                await ApplySkinAsync(_pendingBase64, _pendingSlim, _skinGeneration);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            _initTask = null;
        }
        finally
        {
            _initializing = false;
        }
    }

    private async Task ApplySkinAsync(string base64Png, bool slim, int generation)
    {
        await EnsureBrowserAsync();
        if (!_ready || generation != _skinGeneration)
            return;

        try
        {
            var model = slim ? "slim" : "default";
            var hex = _backgroundHex.Replace("'", "");
            var script =
                $"window.__setSkin && window.__setSkin('data:image/png;base64,{base64Png}','{model}');" +
                $"window.__setBackground && window.__setBackground('{hex}')";
            await Browser.ExecuteScriptAsync(script);
            if (generation == _skinGeneration)
                StatusText.Text = "";
        }
        catch (Exception ex)
        {
            if (generation == _skinGeneration)
                StatusText.Text = ex.Message;
        }
    }

    private async Task ApplyBackgroundAsync()
    {
        await EnsureBrowserAsync();
        if (!_ready)
            return;

        try
        {
            Browser.DefaultBackgroundColor = HexToDrawingColor(_backgroundHex);
            var hex = _backgroundHex.Replace("'", "");
            await Browser.ExecuteScriptAsync($"window.__setBackground && window.__setBackground('{hex}')");
        }
        catch
        {
            // ignore while viewer starts
        }
    }

    private async Task ResizeViewerAsync(int width, int height)
    {
        if (!_ready)
            return;
        try
        {
            await Browser.ExecuteScriptAsync($"window.__resize && window.__resize({width},{height})");
        }
        catch
        {
            // ignore resize failures while viewer is still starting
        }
    }

    internal static string EnsureLocalSkinviewBundle(string? minecraftDir)
    {
        var dir = WebView2Helper.GetUserDataFolder(minecraftDir);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, BundleFileName);
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            return dir;

        using var stream = typeof(MinecraftSkinPreview3D).Assembly
            .GetManifestResourceStream($"Apeiron.Assets.{BundleFileName}");
        if (stream == null)
            throw new InvalidOperationException("Embedded skinview3d bundle is missing.");

        using var fs = File.Create(dest);
        stream.CopyTo(fs);
        return dir;
    }

    private static string BuildShellHtml(string backgroundHex)
    {
        var bg = SettingsService.NormalizeHexColor(backgroundHex, "#07090D");
        var bgJs = "0x" + bg[1..];
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.Append($"<style>html,body{{margin:0;width:100%;height:100%;background:{bg};overflow:hidden;}}");
        sb.Append("#c{display:block;margin:0 auto;}</style></head><body>");
        sb.Append("<canvas id='c'></canvas>");
        sb.Append($"<script src='https://{VirtualHost}/{BundleFileName}'></script>");
        sb.Append("<script>");
        sb.Append("let viewer=null;");
        sb.Append($"let bgColor={bgJs};");
        sb.Append("function applyBg(){");
        sb.Append("const css='#'+bgColor.toString(16).padStart(6,'0');");
        sb.Append("document.documentElement.style.background=css;");
        sb.Append("document.body.style.background=css;");
        sb.Append("if(!viewer)return;");
        // EffectComposer/FXAA makes canvas opaque — scene background must be a solid color.
        sb.Append("viewer.background=bgColor;");
        sb.Append("try{viewer.renderer.setClearColor(bgColor,1);}catch(e){}");
        sb.Append("}");
        sb.Append("async function ensureViewer(){");
        sb.Append("if(viewer)return viewer;");
        sb.Append("const canvas=document.getElementById('c');");
        sb.Append("viewer=new skinview3d.SkinViewer({canvas:canvas,width:window.innerWidth,height:window.innerHeight,background:bgColor});");
        sb.Append("viewer.animation=new skinview3d.WalkingAnimation();");
        sb.Append("viewer.animation.speed=0.8;");
        sb.Append("viewer.controls.enableZoom=true;");
        sb.Append("viewer.controls.enablePan=false;");
        sb.Append("viewer.fov=50;viewer.zoom=0.85;");
        sb.Append("applyBg();");
        sb.Append("return viewer;");
        sb.Append("}");
        sb.Append("window.__resize=function(w,h){if(!viewer)return;viewer.width=w;viewer.height=h;};");
        sb.Append("window.__setBackground=function(hex){");
        sb.Append("if(!hex||typeof hex!=='string')return;");
        sb.Append("const cleaned=hex.replace('#','').trim();");
        sb.Append("if(cleaned.length!==6)return;");
        sb.Append("bgColor=parseInt(cleaned,16);");
        sb.Append("if(Number.isNaN(bgColor))return;");
        sb.Append("applyBg();");
        sb.Append("};");
        sb.Append("window.__setSkin=async function(url,model){");
        sb.Append("try{const v=await ensureViewer();await v.loadSkin(url,{model:model});applyBg();}");
        sb.Append("catch(e){console.error(e);}");
        sb.Append("};");
        sb.Append("window.addEventListener('resize',()=>{if(viewer){viewer.width=window.innerWidth;viewer.height=window.innerHeight;}});");
        sb.Append("</script></body></html>");
        return sb.ToString();
    }

    private static System.Drawing.Color HexToDrawingColor(string hex)
    {
        var h = SettingsService.NormalizeHexColor(hex, "#07090D");
        var r = Convert.ToInt32(h.Substring(1, 2), 16);
        var g = Convert.ToInt32(h.Substring(3, 2), 16);
        var b = Convert.ToInt32(h.Substring(5, 2), 16);
        return System.Drawing.Color.FromArgb(255, r, g, b);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
