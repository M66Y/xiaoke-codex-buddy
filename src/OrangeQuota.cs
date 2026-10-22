using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public class OrangeQuota : Application {
    static string Dir = AppDomain.CurrentDomain.BaseDirectory;
    static JavaScriptSerializer Json = new JavaScriptSerializer();
    Window doll, popup;
    Image image;
    ScaleTransform scale = new ScaleTransform(1, 1);
    ScaleTransform bubbleScale = new ScaleTransform(1,1);
    FrameworkElement bubbleVisual;
    StackPanel cards;
    TextBlock status;
    TextBlock speech;
    TimeSpan careElapsed=TimeSpan.Zero;
    readonly Stopwatch careClock=Stopwatch.StartNew();
    int greetingIndex;
    double artworkTopInBox;
    string greeting="";
    Button refresh;
    bool busy, dragging, preview;
    bool hostWasVisible;
    IntPtr trackedHost = IntPtr.Zero;
    uint trackedHostPid;
    IntPtr lastForeground = IntPtr.Zero;
    bool lastForegroundIsHost;
    static int ownProcessId = Process.GetCurrentProcess().Id;
    DateTime lastAttemptUtc = DateTime.MinValue;
    DateTime lastSuccessLocal = DateTime.MinValue;
    Point down, origin;
    string error = "点击人偶，查询当前额度";
    List<Row> rows = new List<Row>();
    DispatcherTimer monitor;
    int dataRevision, renderedRevision=-1;
    IntPtr mouseHook=IntPtr.Zero;
    MouseHookProc mouseHookCallback;
    int popupGeneration;
    readonly Stopwatch popupIdle=new Stopwatch();
    bool bubbleClosing;
    static Mutex instance;
    public class Row { public string Name; public double? Left; public long? Reset; }
    static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
    static TextBlock Text(string text, double size, string color) {
        return new TextBlock { Text=text, FontSize=size, Foreground=Brush(color), FontFamily=new FontFamily("Microsoft YaHei UI"), TextWrapping=TextWrapping.Wrap };
    }
    static Button Button(string text, RoutedEventHandler handler) {
        var b = new Button { Content=text, Cursor=Cursors.Hand, BorderThickness=new Thickness(0), Padding=new Thickness(12,6,12,6), Background=Brush("#FFF2E8"), Foreground=Brush("#986348"), FontSize=12, FontFamily=new FontFamily("Microsoft YaHei UI") };
        b.Click += handler; return b;
    }
    [STAThread] public static void Main(string[] args) {
        if (args.Contains("--self-test")) { SelfTest(); return; }
        bool created;
        instance = new Mutex(true, "Local\\ClaudeGirlCodexQuotaV12", out created);
        if (!created) return;
        if (args.Contains("--query-check")) {
            try { var result=Parse(ReadQuota()); File.WriteAllText(Path.Combine(Dir,"query-check.txt"),Json.Serialize(result.Select(r=>new {name=r.Name,remaining=r.Left,reset=r.Reset}).ToArray())); }
            catch(Exception ex) { File.WriteAllText(Path.Combine(Dir,"query-check.txt"),ex.Message); }
            return;
        }
        var app = new OrangeQuota();
        app.preview = args.Contains("--preview");
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Startup += delegate { app.Build(); };
        app.Run();
        instance.ReleaseMutex();
    }
    void Build() {
        doll = new Window { Title="Q版 Claude娘 Codex 额度查询-v1.2", Width=212, Height=230, WindowStyle=WindowStyle.None, ResizeMode=ResizeMode.NoResize, AllowsTransparency=true, Background=Brush("#00000000"), ShowInTaskbar=false, Topmost=true, ShowActivated=false };
        var grid = new Grid { Background=null };
        var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.UriSource=new Uri(Path.Combine(Dir,"doll.png")); bitmap.EndInit(); bitmap.Freeze();
        image = new Image { Source=bitmap, Width=190, Height=206, Stretch=Stretch.Uniform, VerticalAlignment=VerticalAlignment.Bottom, HorizontalAlignment=HorizontalAlignment.Center, RenderTransformOrigin=new Point(.5,1), RenderTransform=scale, Cursor=Cursors.Hand, ToolTip="点击查询额度 · 拖动调整位置 · 右键设置" };
        var pixels=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);
        int stride=pixels.PixelWidth*4; var buffer=new byte[stride*pixels.PixelHeight]; pixels.CopyPixels(buffer,stride,0);
        int topRow=0; bool found=false;
        for(int y=0;y<pixels.PixelHeight && !found;y++) for(int x=0;x<pixels.PixelWidth;x++) if(buffer[y*stride+x*4+3]>=32) { topRow=y; found=true; break; }
        double drawnHeight=Math.Min(image.Height,image.Width*pixels.PixelHeight/pixels.PixelWidth);
        artworkTopInBox=(image.Height-drawnHeight)/2+drawnHeight*topRow/pixels.PixelHeight;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        grid.Children.Add(image); doll.Content=grid;
        var work = SystemParameters.WorkArea;
        doll.Left=work.Right-228; doll.Top=work.Bottom-245;
        try {
            var pos=Json.Deserialize<Dictionary<string,double>>(File.ReadAllText(Path.Combine(Dir,"position-native.json")));
            doll.Left=Math.Max(work.Left,Math.Min(pos["x"],work.Right-212));
            doll.Top=Math.Max(work.Top,Math.Min(pos["y"],work.Bottom-230));
        } catch { }
        image.MouseLeftButtonDown += delegate(object s,MouseButtonEventArgs e) {
            down=doll.PointToScreen(e.GetPosition(doll)); origin=new Point(doll.Left,doll.Top); dragging=false; image.CaptureMouse(); e.Handled=true;
        };
        image.MouseMove += delegate(object s,MouseEventArgs e) {
            if (!image.IsMouseCaptured || e.LeftButton!=MouseButtonState.Pressed) return;
            var p=doll.PointToScreen(e.GetPosition(doll));
            var matrix=PresentationSource.FromVisual(doll).CompositionTarget.TransformFromDevice;
            var delta=matrix.Transform(new Vector(p.X-down.X,p.Y-down.Y));
            if (Math.Abs(delta.X)+Math.Abs(delta.Y)>5) dragging=true;
            if (dragging) { doll.Left=Math.Max(work.Left,Math.Min(origin.X+delta.X,work.Right-doll.Width)); doll.Top=Math.Max(work.Top,Math.Min(origin.Y+delta.Y,work.Bottom-doll.Height)); PositionPopup(); }
        };
        image.MouseLeftButtonUp += delegate {
            if (!image.IsMouseCaptured) return;
            bool wasDragging=dragging;
            image.ReleaseMouseCapture();
            if (wasDragging) SavePosition(); else Click();
        };
        image.LostMouseCapture += delegate { dragging=false; };
        var menu=new ContextMenu();
        AddMenu(menu,"查询额度",delegate { Click(); });
        AddMenu(menu,"收起卡片",delegate { HidePopup(); });
        AddMenu(menu,"退出人偶",delegate { Shutdown(); });
        image.ContextMenu=menu;
        if(preview || HostForeground()) doll.Show();
        monitor=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(150) };
        monitor.Tick+=delegate {
            bool visible=preview || HostForeground();
            if(visible && !doll.IsVisible) doll.Show();
            if(!visible && (doll.IsVisible || (popup!=null && popup.IsVisible))) HideOverlay();
            if(ShouldAutoHide(popup!=null && popup.IsVisible,bubbleClosing,popupIdle.Elapsed) && !image.IsMouseCaptured && !image.ContextMenu.IsOpen) AnimateCloseBubble();
            if(visible && (!hostWasVisible || (DateTime.UtcNow-lastAttemptUtc).TotalSeconds>=60)) Query(false);
            var elapsed=careClock.Elapsed; careClock.Restart();
            if(AdvanceCare(elapsed,visible,popup!=null && popup.IsVisible)) {
                if(!dragging && !image.IsMouseCaptured && !image.ContextMenu.IsOpen) {
                    NextGreeting(); Bounce(); ShowPopup(); Query(false);
                } else careElapsed=TimeSpan.FromMinutes(5);
            }
            hostWasVisible=visible;
        };
        monitor.Start();
        Exit+=delegate { StopMouseHook(); SavePosition(); };
        if(Environment.GetCommandLineArgs().Contains("--idle-test")) {
            busy=true; greeting="交互验证 · 示例数据";
            ShowPopup(); AnimateCloseBubble(); TouchPopup();
            var verifyCancel=new DispatcherTimer { Interval=TimeSpan.FromSeconds(.6) };
            verifyCancel.Tick+=delegate { verifyCancel.Stop();
                if(!popup.IsVisible || bubbleClosing) { File.WriteAllText(Path.Combine(Dir,"idle-check.txt"),"FAIL: clicking during close did not cancel it"); Shutdown(); }
            }; verifyCancel.Start();
            var verifyIdle=new DispatcherTimer { Interval=TimeSpan.FromSeconds(6.9) };
            verifyIdle.Tick+=delegate { verifyIdle.Stop();
                File.WriteAllText(Path.Combine(Dir,"idle-check.txt"),!popup.IsVisible && mouseHook==IntPtr.Zero && !bubbleClosing?"PASS: click cancels pending dismissal; six-second inactivity closes with animation and releases hook":"FAIL: inactivity dismissal did not complete"); Shutdown();
            }; verifyIdle.Start();
        }
        if(Environment.GetCommandLineArgs().Contains("--smoke-test")) {
            rows=new List<Row> { new Row{Name="5 小时额度",Left=51,Reset=1800000000},new Row{Name="每周额度",Left=48,Reset=1800200000} };
            lastSuccessLocal=DateTime.Now.AddSeconds(-30);
            greeting="枣尚豪！今天也要开开心心地过呀！";
            BeginQueryState(); error="界面验证 · 示例数据 · 正在后台更新…"; ShowPopup(); Bounce();
            var testTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(900) };
            testTimer.Tick+=delegate { testTimer.Stop(); try {
                if(cards.Children.Count!=2 || !busy) throw new Exception("refresh cleared visible cards");
                if(Math.Abs(bubbleScale.ScaleX-1)>.001 || Math.Abs(bubbleScale.ScaleY-1)>.001) throw new Exception("bubble animation did not settle");
                var testHost=new Window { Width=20, Height=20, Left=-10000, Top=-10000, ShowActivated=false, ShowInTaskbar=false, WindowStyle=WindowStyle.ToolWindow };
                testHost.Show();
                var handle=new System.Windows.Interop.WindowInteropHelper(testHost).Handle;
                if(!TrackedHostVisible(handle)) throw new Exception("visible native host state failed");
                testHost.WindowState=WindowState.Minimized;
                if(TrackedHostVisible(handle)) throw new Exception("minimized native host state failed");
                testHost.Close();
                if(TrackedHostVisible(handle)) throw new Exception("closed native host state failed");
                if(mouseHook==IntPtr.Zero) throw new Exception("outside click hook installation failed");
                var originalPopup=popup; var originalCard=cards.Children[0];
                HideOverlay();
                if(doll.IsVisible || popup.IsVisible || mouseHook!=IntPtr.Zero || image.IsMouseCaptured || dragging || scale.ScaleX!=1 || scale.ScaleY!=1) throw new Exception("hide did not reset interaction state");
                var restoreTime=Stopwatch.StartNew();
                for(int cycle=0;cycle<10;cycle++) { doll.Show(); ShowPopup(); HideOverlay(); }
                doll.Show(); ShowPopup(); restoreTime.Stop();
                if(!bubbleScale.HasAnimatedProperties) throw new Exception("bubble animation was not attached on open");
                if(!Object.ReferenceEquals(originalPopup,popup) || !Object.ReferenceEquals(originalCard,cards.Children[0])) throw new Exception("restore rebuilt cached UI");
                if(restoreTime.ElapsedMilliseconds>3000) throw new Exception("restore regression exceeded 3 seconds for 10 cycles");
                QueueDismissForClick((uint)ownProcessId); DrainTestDispatcher();
                if(!popup.IsVisible) throw new Exception("inside click incorrectly dismissed popup");
                QueueDismissForClick((uint)ownProcessId+1); DrainTestDispatcher();
                if(popup.IsVisible || mouseHook!=IntPtr.Zero) throw new Exception("outside click did not hide popup and release hook");
                ShowPopup();
                Capture(); File.WriteAllText(Path.Combine(Dir,"ui-check.txt"), "PASS: native outside-click hook; hide resets mouse and animation; cached popup/card reused across 10 restore cycles in "+restoreTime.ElapsedMilliseconds+" ms; layout and refresh regression; sample data only.");
            } catch(Exception ex) { File.WriteAllText(Path.Combine(Dir,"ui-check.txt"),ex.ToString()); } Shutdown(); };
            testTimer.Start();
        }
    }
    void SavePosition() { try { File.WriteAllText(Path.Combine(Dir,"position-native.json"),Json.Serialize(new {x=doll.Left,y=doll.Top})); } catch {} }
    void AddMenu(ContextMenu menu,string text,RoutedEventHandler action) { var m=new MenuItem { Header=text }; m.Click+=action; menu.Items.Add(m); }
    void Bounce() {
        double[] times={0,.07,.17,.27,.37,.47,.58};
        double[] xs={1,1.15,.89,1.065,.97,1.012,1};
        double[] ys={1,.84,1.13,.94,1.03,.99,1};
        var ax=new DoubleAnimationUsingKeyFrames { FillBehavior=FillBehavior.Stop }; var ay=new DoubleAnimationUsingKeyFrames { FillBehavior=FillBehavior.Stop };
        for(int i=0;i<times.Length;i++) {
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),new SineEase{EasingMode=EasingMode.EaseInOut}));
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(ys[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),new SineEase{EasingMode=EasingMode.EaseInOut}));
        }
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,ax); scale.BeginAnimation(ScaleTransform.ScaleYProperty,ay);
    }
    static string GreetingForHour(int hour) {
        if(hour>=5 && hour<11) return "枣尚豪！今天也要开开心心地过呀！";
        if(hour>=11 && hour<14) return "中午好呀！记得好好吃饭，补充一点能量～";
        if(hour>=14 && hour<18) return "下午好呀！喝口水、伸个懒腰，我们慢慢来～";
        if(hour>=18 && hour<23) return "晚上好呀！今天辛苦啦，也给自己留一点放松的时间。";
        return "夜深啦！记得照顾自己，困了就早点休息呀。";
    }
    void NextGreeting() {
        string[] messages={GreetingForHour(DateTime.Now.Hour),"歇一小会儿吧，看看远处，让眼睛也放个小假～","喝口水呀！不用着急，一步一步来也很好。","今天也有在认真努力呢，别忘了对自己温柔一点～"};
        greeting=messages[greetingIndex++%messages.Length];
        if(speech!=null) speech.Text=greeting;
    }
    bool AdvanceCare(TimeSpan elapsed,bool visible,bool popupOpen) {
        if(!visible) return false;
        careElapsed+=elapsed;
        if(careElapsed<TimeSpan.FromMinutes(5)) return false;
        careElapsed=TimeSpan.Zero;
        return !popupOpen;
    }
    void Click() { careElapsed=TimeSpan.Zero; NextGreeting(); Bounce(); ShowPopup(); Query(false); }
    void HidePopup() { popupGeneration++; popupIdle.Reset(); StopMouseHook(); ResetBubbleMotion(); if(popup!=null) popup.Hide(); }
    static bool ShouldAutoHide(bool visible,bool closing,TimeSpan idle) { return visible && !closing && idle>=TimeSpan.FromSeconds(6); }
    void TouchPopup() {
        popupIdle.Restart();
        if(bubbleClosing) { popupGeneration++; ResetBubbleMotion(); }
    }
    void AnimateCloseBubble() {
        if(popup==null || !popup.IsVisible || bubbleClosing) return;
        ResetBubbleMotion(); bubbleClosing=true;
        int generation=popupGeneration;
        double[] times={0,.08,.15,.34};
        double[] xs={1,.97,1,.15}, ys={1,.93,1,.12};
        var ax=new DoubleAnimationUsingKeyFrames(); var ay=new DoubleAnimationUsingKeyFrames();
        for(int i=0;i<times.Length;i++) {
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),new SineEase { EasingMode=EasingMode.EaseInOut }));
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(ys[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),new SineEase { EasingMode=EasingMode.EaseInOut }));
        }
        ay.Completed+=delegate { if(generation==popupGeneration && bubbleClosing) HidePopup(); };
        bubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty,ax);
        bubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty,ay);
        bubbleVisual.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromSeconds(.19)) { BeginTime=TimeSpan.FromSeconds(.15) });
    }
    void ResetBubbleMotion() {
        bubbleClosing=false;
        bubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
        bubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        if(bubbleVisual!=null) bubbleVisual.BeginAnimation(UIElement.OpacityProperty,null);
    }
    void AnimateBubble() {
        double[] times={0,.12,.22,.32,.43};
        double[] xs={.89,1,.975,1,1};
        double[] ys={.84,1,.955,1,1};
        var ax=new DoubleAnimationUsingKeyFrames { FillBehavior=FillBehavior.Stop };
        var ay=new DoubleAnimationUsingKeyFrames { FillBehavior=FillBehavior.Stop };
        for(int i=0;i<times.Length;i++) {
            ax.KeyFrames.Add(new EasingDoubleKeyFrame(xs[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),new SineEase { EasingMode=EasingMode.EaseInOut }));
            ay.KeyFrames.Add(new EasingDoubleKeyFrame(ys[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])),new SineEase { EasingMode=EasingMode.EaseInOut }));
        }
        bubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty,ax);
        bubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty,ay);
        bubbleVisual.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.25,1,TimeSpan.FromSeconds(.12)) { FillBehavior=FillBehavior.Stop });
    }
    void HideOverlay() {
        HidePopup();
        if(image.IsMouseCaptured) image.ReleaseMouseCapture();
        dragging=false;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null); scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        scale.ScaleX=1; scale.ScaleY=1;
        doll.Hide();
    }
    void ShowPopup() {
        bool opening=popup==null || !popup.IsVisible;
        TouchPopup();
        if(popup==null) {
            popup=new Window { Title="小克的小纸条", Width=300, SizeToContent=SizeToContent.Height, WindowStyle=WindowStyle.None, ResizeMode=ResizeMode.NoResize, AllowsTransparency=true, Background=Brush("#00000000"), ShowInTaskbar=false, Topmost=true, ShowActivated=false, Owner=doll };
            var border=new Border { Background=Brush("#FFFCF8"), CornerRadius=new CornerRadius(16), BorderBrush=Brush("#EEDBCB"), BorderThickness=new Thickness(1), Padding=new Thickness(16,13,16,12) };
            var panel=new StackPanel(); border.Child=panel;
            var bubble=new Grid { Margin=new Thickness(0,0,0,12) }; bubble.Children.Add(border);
            bubbleVisual=bubble; bubble.RenderTransform=bubbleScale; bubble.RenderTransformOrigin=new Point(.82,1);
            bubble.Children.Add(new Border { Width=24, Height=3,Background=Brush("#FFFCF8"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,49,-1) });
            var tail=new System.Windows.Shapes.Path { Data=Geometry.Parse("M 0 0 L 16 14 L 25 0"), Fill=Brush("#FFFCF8"), Stroke=Brush("#EEDBCB"), StrokeThickness=1, Width=26, Height=15, HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Bottom, Margin=new Thickness(0,0,48,-13) };
            bubble.Children.Add(tail); popup.Content=bubble;
            var head=new DockPanel();
            var close=Button("×",delegate { HidePopup(); }); close.Padding=new Thickness(8,0,8,2); close.FontSize=20; close.Background=Brush("#FFFCF8"); DockPanel.SetDock(close,Dock.Right); head.Children.Add(close);
            var title=Text("小克的小纸条",12,"#A27D64"); title.VerticalAlignment=VerticalAlignment.Center; head.Children.Add(title); panel.Children.Add(head);
            speech=Text(String.IsNullOrEmpty(greeting)?GreetingForHour(DateTime.Now.Hour):greeting,15,"#674431"); speech.FontWeight=FontWeights.Medium; speech.LineHeight=24; speech.Margin=new Thickness(0,8,0,12); panel.Children.Add(speech);
            var divider=new Border { Height=1,Background=Brush("#F1E6DB"),Margin=new Thickness(0,0,0,10) }; panel.Children.Add(divider);
            panel.Children.Add(Text("帮你看了一眼 Codex 额度",11,"#967D6A"));
            status=Text("",10,"#A18B7B"); status.Margin=new Thickness(0,4,0,10); panel.Children.Add(status);
            var scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, MaxHeight=250 };
            cards=new StackPanel(); scroll.Content=cards; panel.Children.Add(scroll);
            refresh=Button("刷新额度",delegate { Query(); }); refresh.Margin=new Thickness(0,10,0,0); refresh.HorizontalAlignment=HorizontalAlignment.Stretch; panel.Children.Add(refresh);
            popup.SizeChanged+=delegate { PositionPopup(); };
            popup.Deactivated+=delegate {
                int generation=popupGeneration;
                Dispatcher.BeginInvoke(new Action(delegate {
                    uint foregroundPid; GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);
                    if(generation==popupGeneration && foregroundPid!=ownProcessId) HidePopup();
                }),DispatcherPriority.Input);
            };
        }
        popupGeneration++; popup.Show(); Render(); PositionPopup(); StartMouseHook();
        if(opening) AnimateBubble();
    }
    void PositionPopup() {
        if(popup==null) return;
        var work=SystemParameters.WorkArea;
        double h=popup.ActualHeight>0?popup.ActualHeight:300;
        popup.Left=Math.Max(work.Left+8,Math.Min(doll.Left-62,work.Right-popup.Width-8));
        // Keep 6 physical pixels clear of the artwork at the tallest bounce frame.
        double bounceTop=doll.Top+doll.Height-(image.Height-artworkTopInBox)*1.13;
        var presentation=PresentationSource.FromVisual(doll);
        double pixelScale=presentation!=null && presentation.CompositionTarget!=null?presentation.CompositionTarget.TransformToDevice.M22:1;
        double above=bounceTop-h-6/Math.Max(1,pixelScale);
        popup.Top=above>=work.Top+8?above:Math.Min(work.Bottom-h-8,doll.Top+30);
    }
    void Render() {
        if(popup==null) return;
        status.Text=error; refresh.IsEnabled=!busy; refresh.Content=busy?"正在读取…":"刷新额度";
        if(!popup.IsVisible || renderedRevision==dataRevision) return;
        renderedRevision=dataRevision;
        cards.Children.Clear();
        foreach(var row in rows) {
            var card=new StackPanel { Margin=new Thickness(0,0,0,10) };
            card.Children.Add(Text(row.Name,11,"#806959"));
            var line=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,2,0,5) };
            var amount=Text(row.Left.HasValue?row.Left.Value.ToString("0.#")+"%":"—",22,"#D98149"); amount.FontWeight=FontWeights.SemiBold; line.Children.Add(amount);
            var suffix=Text(row.Left.HasValue?"剩余":"暂不可用",11,"#A18B7B"); suffix.VerticalAlignment=VerticalAlignment.Bottom; suffix.Margin=new Thickness(8,0,0,6); line.Children.Add(suffix); card.Children.Add(line);
            var bar=new Border { Height=5, CornerRadius=new CornerRadius(2.5), Background=Brush("#F0E5DA"), ClipToBounds=true };
            var fill=new Border { Height=5, CornerRadius=new CornerRadius(2.5), Background=Brush(row.Left.HasValue && row.Left<15?"#C66450":"#E6A171"), HorizontalAlignment=HorizontalAlignment.Left, Width=0 };
            bar.Child=fill; bar.SizeChanged+=delegate { fill.Width=bar.ActualWidth*(row.Left??0)/100; }; card.Children.Add(bar);
            string reset="重置时间未提供";
            if(row.Reset.HasValue) { try { reset=DateTimeOffset.FromUnixTimeSeconds(row.Reset.Value).ToLocalTime().ToString("MM-dd HH:mm")+" 重置"; } catch{} }
            var resetText=Text(reset,10,"#AC9685"); resetText.Margin=new Thickness(0,4,0,0); card.Children.Add(resetText); cards.Children.Add(card);
        }
        PositionPopup();
    }
    bool ShouldQuery(bool force) {
        return !busy && (force || lastSuccessLocal==DateTime.MinValue || (DateTime.Now-lastSuccessLocal).TotalSeconds>=15);
    }
    void BeginQueryState() {
        busy=true; lastAttemptUtc=DateTime.UtcNow;
        error=rows.Count>0?"上次更新 "+lastSuccessLocal.ToString("HH:mm:ss")+" · 正在更新…":"首次查询中，稍后自动显示…";
    }
    void CompleteQueryState(List<Row> result,string failure) {
        busy=false;
        if(failure==null) {
            rows=result??new List<Row>(); dataRevision++; lastSuccessLocal=DateTime.Now;
            error=rows.Count>0?"更新于 "+lastSuccessLocal.ToString("HH:mm:ss"):"当前账户未返回可用额度窗口";
        } else {
            error=rows.Count>0?"保留 "+lastSuccessLocal.ToString("HH:mm:ss")+" 的数据 · 更新失败："+failure:failure;
        }
    }
    void Query(bool force=true) {
        if(!ShouldQuery(force)) return;
        BeginQueryState(); Render();
        Task.Factory.StartNew(delegate {
            List<Row> result=null; string failure=null;
            try { result=Parse(ReadQuota()); } catch(Exception ex) { failure=ex.Message; }
            Dispatcher.BeginInvoke(new Action(delegate {
                CompleteQueryState(result,failure); Render();
            }));
        });
    }
    static Dictionary<string,object> Dict(object o) { return o as Dictionary<string,object>??new Dictionary<string,object>(); }
    static object Get(Dictionary<string,object> d,string k) { object v; return d.TryGetValue(k,out v)?v:null; }
    public static List<Row> Parse(Dictionary<string,object> data) {
        var buckets=Dict(Get(data,"rateLimitsByLimitId"));
        if(buckets.Count==0) buckets=new Dictionary<string,object>{{"codex",Get(data,"rateLimits")}};
        var list=new List<Row>();
        foreach(var entry in buckets) {
            var bucket=Dict(entry.Value);
            foreach(var key in new[]{"primary","secondary"}) {
                var w=Dict(Get(bucket,key)); if(w.Count==0) continue;
                double used,minutes; double? left=null;
                var value=Get(w,"usedPercent");
                if(value!=null && !(value is bool) && Double.TryParse(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out used) && !Double.IsNaN(used) && !Double.IsInfinity(used) && used>=0 && used<=100) left=100-used;
                string label="额度窗口";
                if(Double.TryParse(Convert.ToString(Get(w,"windowDurationMins")),out minutes)) label=minutes==10080?"每周额度":minutes==300?"5 小时额度":minutes+" 分钟额度";
                if(buckets.Count>1) label=(Get(bucket,"limitName")??entry.Key)+" · "+label;
                long reset; long? timestamp=null;
                if(Int64.TryParse(Convert.ToString(Get(w,"resetsAt")),out reset)) timestamp=reset;
                list.Add(new Row{Name=label,Left=left,Reset=timestamp});
            }
        }
        return list;
    }
    static string FindCodex() {
        var paths=(Environment.GetEnvironmentVariable("PATH")??"").Split(';');
        foreach(var p in paths) { try { var exe=Path.Combine(p,"codex.exe"); if(File.Exists(exe)) return exe; } catch{} }
        var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
        if(Directory.Exists(dir)) {
            var exes=Directory.GetFiles(dir,"codex.exe",SearchOption.AllDirectories);
            if(exes.Length>0) return exes.OrderByDescending(File.GetLastWriteTimeUtc).First();
        }
        throw new Exception("未找到本机 Codex，请先安装并登录。");
    }
    static Dictionary<string,object> ReadQuota() {
        using(var p=new Process()) {
            p.StartInfo=new ProcessStartInfo(FindCodex(),"app-server --listen stdio://") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
            p.Start(); p.ErrorDataReceived+=delegate{}; p.BeginErrorReadLine();
            try {
                var task=Task.Factory.StartNew(delegate {
                    p.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"orange_quota\",\"version\":\"1.2.0\"}}}"); p.StandardInput.Flush();
                    Receive(p,1);
                    p.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                    p.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}"); p.StandardInput.Flush();
                    return Receive(p,2);
                });
                try { if(!task.Wait(25000)) throw new Exception("查询超时，请稍后重试。"); return task.Result; }
                catch(AggregateException ex) { throw ex.GetBaseException(); }
            } finally { try { if(!p.HasExited) p.Kill(); p.WaitForExit(3000); } catch{} }
        }
    }
    static Dictionary<string,object> Receive(Process p,int id) {
        string line;
        while((line=p.StandardOutput.ReadLine())!=null) {
            Dictionary<string,object> msg;
            try { msg=Json.Deserialize<Dictionary<string,object>>(line); } catch {continue;}
            if(Convert.ToString(Get(msg,"id"))!=id.ToString()) continue;
            if(msg.ContainsKey("error")) {
                var reason=Convert.ToString(Get(Dict(msg["error"]),"message"));
                if(reason.IndexOf("authentication required",StringComparison.OrdinalIgnoreCase)>=0) reason="本机 Codex CLI 需要登录，请执行 codex login 后重试。";
                throw new Exception(reason);
            }
            return Dict(Get(msg,"result"));
        }
        throw new Exception("本机 Codex 服务已退出，请重试。");
    }
    delegate IntPtr MouseHookProc(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X,Y; }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,MouseHookProc callback,IntPtr module,uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    static bool ShouldDismissPopup(bool visible,uint clickedPid) { return visible && clickedPid!=ownProcessId; }
    void StartMouseHook() {
        if(mouseHook!=IntPtr.Zero) return;
        if(mouseHookCallback==null) mouseHookCallback=OnMouse;
        mouseHook=SetWindowsHookEx(14,mouseHookCallback,GetModuleHandle(null),0);
        if(mouseHook==IntPtr.Zero) { error="外部点击检测未能启用，请使用右上角 × 收起"; status.Text=error; }
    }
    void StopMouseHook() {
        if(mouseHook!=IntPtr.Zero) { UnhookWindowsHookEx(mouseHook); mouseHook=IntPtr.Zero; }
    }
    static void DrainTestDispatcher() {
        var frame=new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(delegate { frame.Continue=false; }),DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
    }
    void QueueDismissForClick(uint pid) {
        if(popup!=null && popup.IsVisible && pid==ownProcessId) TouchPopup();
        if(ShouldDismissPopup(popup!=null && popup.IsVisible,pid)) {
            int generation=popupGeneration;
            Dispatcher.BeginInvoke(new Action(delegate { if(generation==popupGeneration) HidePopup(); }),DispatcherPriority.Input);
        }
    }
    IntPtr OnMouse(int code,IntPtr message,IntPtr data) {
        int m=message.ToInt32();
        if(code>=0 && (m==0x201 || m==0x204 || m==0x207 || m==0x20B)) {
            var point=new NativePoint { X=Marshal.ReadInt32(data),Y=Marshal.ReadInt32(data,4) };
            uint pid; GetWindowThreadProcessId(GetAncestor(WindowFromPoint(point),2),out pid);
            QueueDismissForClick(pid);
        }
        return CallNextHookEx(mouseHook,code,message,data);
    }
    delegate bool EnumWindowProc(IntPtr window,IntPtr arg);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr arg);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    static bool VisibleHostState(bool foregroundIsHost,bool foregroundIsOurs,bool hostIsVisible) {
        return hostIsVisible && (foregroundIsHost || foregroundIsOurs);
    }
    static bool TrackedHostVisible(IntPtr handle) {
        return handle!=IntPtr.Zero && IsWindow(handle) && IsWindowVisible(handle) && !IsIconic(handle);
    }
    bool HostForeground() {
        var foreground=GetAncestor(GetForegroundWindow(),2);
        if(foreground==IntPtr.Zero) return false;
        uint pid; GetWindowThreadProcessId(foreground,out pid);
        if(foreground!=lastForeground) {
            lastForeground=foreground;
            lastForegroundIsHost=foreground==trackedHost && pid==trackedHostPid;
            if(!lastForegroundIsHost && pid!=ownProcessId) try {
                using(var process=Process.GetProcessById((int)pid)) {
                    lastForegroundIsHost=process.ProcessName.Equals("Codex",StringComparison.OrdinalIgnoreCase) || process.ProcessName.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase);
                }
            } catch{}
        }
        if(lastForegroundIsHost) { trackedHost=foreground; trackedHostPid=pid; }
        bool hostVisible=TrackedHostVisible(trackedHost);
        return VisibleHostState(lastForegroundIsHost,pid==ownProcessId,hostVisible);
    }
    static void SelfTest() {
        var d=Json.Deserialize<Dictionary<string,object>>("{\"rateLimits\":{\"primary\":{\"usedPercent\":49,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":52,\"windowDurationMins\":10080}}}");
        var a=Parse(d); if(a.Count!=2 || a[0].Left!=51 || a[1].Left!=48) throw new Exception("quota parsing failed");
        if(Parse(new Dictionary<string,object>()).Count!=0) throw new Exception("empty failed");
        foreach(string value in new[]{"null","true","-1","101"}) {
            d=Json.Deserialize<Dictionary<string,object>>("{\"rateLimits\":{\"primary\":{\"usedPercent\":"+value+"}}}");
            if(Parse(d)[0].Left!=null) throw new Exception("invalid quota failed");
        }
        var app=new OrangeQuota();
        if(!app.ShouldQuery(false)) throw new Exception("cold start must query");
        app.CompleteQueryState(a,null);
        if(app.ShouldQuery(false) || !app.ShouldQuery(true)) throw new Exception("fresh cache deduplication failed");
        app.BeginQueryState();
        if(app.rows.Count!=2 || app.ShouldQuery(true)) throw new Exception("in-flight cache preservation failed");
        app.CompleteQueryState(null,"network unavailable");
        if(app.rows.Count!=2 || !app.error.Contains("更新失败")) throw new Exception("error preservation failed");
        app.lastSuccessLocal=DateTime.Now.AddSeconds(-61);
        if(!app.ShouldQuery(false)) throw new Exception("stale cache must refresh");
        app.CompleteQueryState(new List<Row>(),null);
        if(app.rows.Count!=0) throw new Exception("empty success must replace old data");
        if(!VisibleHostState(true,false,true)) throw new Exception("foreground host must show mascot");
        if(!VisibleHostState(false,true,true)) throw new Exception("own popup must stay interactive");
        if(VisibleHostState(false,false,true)) throw new Exception("other application must hide mascot");
        if(VisibleHostState(true,false,false) || VisibleHostState(false,true,false)) throw new Exception("minimized/closed host must hide mascot and popup");
        if(!ShouldDismissPopup(true,(uint)ownProcessId+1)) throw new Exception("click outside must dismiss popup");
        if(ShouldDismissPopup(true,(uint)ownProcessId) || ShouldDismissPopup(false,0)) throw new Exception("inside click or hidden popup should not dismiss");
        if(app.AdvanceCare(TimeSpan.FromSeconds(299),true,false)) throw new Exception("care reminder fired early");
        if(app.AdvanceCare(TimeSpan.FromHours(1),false,false)) throw new Exception("care reminder fired in background");
        if(!app.AdvanceCare(TimeSpan.FromSeconds(1),true,false)) throw new Exception("care reminder missed five active minutes");
        if(app.AdvanceCare(TimeSpan.FromMinutes(5),true,true)) throw new Exception("care reminder replaced an open conversation");
        if(app.AdvanceCare(TimeSpan.FromSeconds(1),true,false)) throw new Exception("care reminder caught up after open popup");
        if(GreetingForHour(8)!="枣尚豪！今天也要开开心心地过呀！") throw new Exception("morning greeting mismatch");
        if(ShouldAutoHide(true,false,TimeSpan.FromMilliseconds(5999)) || !ShouldAutoHide(true,false,TimeSpan.FromSeconds(6))) throw new Exception("six-second idle boundary failed");
        if(ShouldAutoHide(false,false,TimeSpan.FromSeconds(9)) || ShouldAutoHide(true,true,TimeSpan.FromSeconds(9))) throw new Exception("hidden/closing popup must not retrigger");
        File.WriteAllText(Path.Combine(Dir,"self-test.txt"),"PASS: five active minutes triggers reminder; background time paused; open popup not replaced; no catch-up; exact morning greeting; foreground, dismissal, cache and quota regression checks.");
        File.WriteAllText(Path.Combine(Dir,"codex-window-check.txt"),app.HostForeground()?"Host foreground detected":"Host is not foreground; overlay should stay hidden");
    }
    void Capture() {
        ResetBubbleMotion();
        // Render our own WPF visuals for QA; no desktop/screen capture.
        var stack=new StackPanel { Background=Brush("#F3F5F4"), Width=popup.Width+48 };
        var previewImage=new Image { Source=image.Source, Width=200, Height=210, Stretch=Stretch.Uniform, Margin=new Thickness(0,12,0,10) };
        var content=(UIElement)popup.Content; popup.Content=null;
        var holder=new Border { Margin=new Thickness(24,18,24,12), Child=content };
        stack.Children.Add(holder); stack.Children.Add(previewImage);
        stack.Measure(new Size(stack.Width,1000)); stack.Arrange(new Rect(stack.DesiredSize)); stack.UpdateLayout();
        var bmp=new RenderTargetBitmap((int)(stack.Width*2),(int)(stack.ActualHeight*2),192,192,PixelFormats.Pbgra32); bmp.Render(stack);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using(var f=File.Create(Path.Combine(Dir,"preview-v0.3.png"))) encoder.Save(f);
    }
}
