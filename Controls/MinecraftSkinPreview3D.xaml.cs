using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Apeiron.Controls;

public partial class MinecraftSkinPreview3D : UserControl
{
    private bool _ready;
    private string? _pendingHtml;
    private string _dragHint = "Drag to rotate";
    private BitmapSource? _lastSkin;
    private bool _lastSlim;
    private int _lastW;
    private int _lastH;

    public MinecraftSkinPreview3D()
    {
        InitializeComponent();
        Loaded += async (_, _) => await EnsureBrowserAsync();
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
            SetSkin(_lastSkin, _lastSlim);
        };
    }

    public void SetDragHint(string text)
    {
        _dragHint = text;
        DragHint.Text = text;
    }

    public void SetSkin(BitmapSource? skin, bool slim)
    {
        _lastSkin = skin;
        _lastSlim = slim;

        if (skin == null)
        {
            StatusText.Text = "";
            _pendingHtml = null;
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

        var w = Math.Max(200, (int)ActualWidth);
        var h = Math.Max(200, (int)ActualHeight);
        if (w < 50) w = 420;
        if (h < 50) h = 300;

        var html = BuildHtml(Convert.ToBase64String(png), slim, w, h);
        _pendingHtml = html;
        StatusText.Text = "";
        _ = NavigateAsync(html);
    }

    private async Task EnsureBrowserAsync()
    {
        try
        {
            StatusText.Text = "...";
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 7, 9, 13);
            _ready = true;
            StatusText.Text = "";
            if (_pendingHtml != null)
                await NavigateAsync(_pendingHtml);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task NavigateAsync(string html)
    {
        if (!_ready)
        {
            await EnsureBrowserAsync();
            if (!_ready)
                return;
        }

        try
        {
            Browser.NavigateToString(html);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private string BuildHtml(string base64Png, bool slim, int width, int height)
    {
        var model = slim ? "slim" : "default";
        var hint = EscapeJs(_dragHint);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/>");
        sb.Append("<style>html,body{margin:0;width:100%;height:100%;background:#07090D;overflow:hidden;}");
        sb.Append("#c{display:block;margin:0 auto;}</style></head><body>");
        sb.Append("<canvas id='c'></canvas>");
        sb.Append("<script src='https://cdn.jsdelivr.net/npm/skinview3d@3.4.2/bundles/skinview3d.bundle.js'></script>");
        sb.Append("<script>(async function(){");
        sb.Append("try{");
        sb.Append("const canvas=document.getElementById('c');");
        sb.Append($"const viewer=new skinview3d.SkinViewer({{canvas:canvas,width:{width},height:{height},background:0x07090d}});");
        sb.Append($"await viewer.loadSkin('data:image/png;base64,{base64Png}',{{model:'{model}'}});");
        sb.Append("viewer.animation=new skinview3d.WalkingAnimation();");
        sb.Append("viewer.animation.speed=0.8;");
        sb.Append("viewer.controls.enableZoom=true;");
        sb.Append("viewer.controls.enablePan=false;");
        sb.Append("viewer.fov=50;viewer.zoom=0.85;");
        sb.Append("window.addEventListener('resize',()=>{");
        sb.Append("viewer.width=window.innerWidth;viewer.height=window.innerHeight;");
        sb.Append("});");
        sb.Append("viewer.width=window.innerWidth;viewer.height=window.innerHeight;");
        sb.Append("}catch(e){document.body.innerHTML='<div style=\"color:#A8B0C0;font:12px Segoe UI;padding:16px;text-align:center\">'+e+'</div>';}");
        sb.Append("})();</script></body></html>");
        _ = hint;
        return sb.ToString();
    }

    private static string EscapeJs(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "").Replace("\n", " ");

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
