using Avalonia;
using Avalonia.Controls;
using System.Runtime.InteropServices;
using Tmds.DBus;

namespace PickerHub;

[DBusInterface("org.freedesktop.portal.ScreenCast")]
public interface IScreenCast : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string,object> options);
    Task<ObjectPath> SelectSourcesAsync(ObjectPath session,IDictionary<string,object> options);
    Task<ObjectPath> StartAsync(ObjectPath session,string parent,IDictionary<string,object> options);
    Task<CloseSafeHandle> OpenPipeWireRemoteAsync(ObjectPath session,IDictionary<string,object> options);
}
[DBusInterface("org.freedesktop.portal.Session")]
public interface ICastSession : IDBusObject
{
    Task CloseAsync();
    Task<IDisposable> WatchClosedAsync(Action<IDictionary<string,object>> callback,Action<Exception>? error=null);
}

/// <summary>One user-approved stream session per app run. Each pick reads a fresh frame.</summary>
internal sealed class WaylandCapture : IDisposable
{
    const string Service="org.freedesktop.portal.Desktop";
    static WaylandCapture? current;
    readonly Connection connection=new(Address.Session);
    readonly List<(GstCapture capture,int x,int y)> sources=[];
    ICastSession? session;
    IDisposable? closed;
    CloseSafeHandle? remote;
    volatile bool ended;
    string peer="";
    public static void ClearPending(){if(current!=null&&!current.ended)foreach(var s in current.sources)s.capture.Drain();}
    public static bool IsSharing => current is { ended: false };
    public static event Action? SharingChanged;
    public static void Stop()
    {
        var old = current;
        current = null;
        if (old == null) return;
        old.Dispose();
        SharingChanged?.Invoke();
    }
    public static async Task<bool> Prepare(Window owner)
    {
        if (current?.ended == true) Stop();
        if (current == null)
        {
            var opened = new WaylandCapture();
            try
            {
                if (!await opened.Open(owner)) { opened.Dispose(); return false; }
                current = opened;
            }
            catch { opened.Dispose(); throw; }
        }
        var screens = owner.Screens.All.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
        if (screens.Length != current.sources.Count)
        {
            Stop();
            throw new InvalidOperationException(L.T("اختر جميع الشاشات المتصلة في نافذة المشاركة. أعد فتح القطّارة لاختيارها."));
        }
        current.monitors = screens.Select(s => s.Bounds).Zip(current.sources.OrderBy(s => s.x).ThenBy(s => s.y),
            (bounds, source) => (bounds, source.capture)).ToDictionary(pair => pair.bounds, pair => pair.capture);
        SharingChanged?.Invoke();
        return true;
    }
    Dictionary<PixelRect, GstCapture> monitors = [];
    public static void DiscardFrame(PixelRect bounds)
    {
        if (current?.monitors.TryGetValue(bounds, out var source) == true) source.Drain();
    }
    public static bool Sample(Window owner, PixelPoint p, int radius, byte[] pixels)
    {
        if (current == null || current.ended)
            throw new IOException(L.T("توقفت مشاركة الشاشة. افتح القطّارة لإعادة الاتصال."));
        var bounds = owner.Screens.ScreenFromPoint(p)?.Bounds;
        if (bounds == null || !current.monitors.TryGetValue(bounds.Value, out var capture))
        {
            Stop();
            throw new IOException(L.T("تغيّر ترتيب الشاشات. افتح القطّارة لاختيار الشاشات مجدداً."));
        }
        try { return capture.Sample(bounds.Value, p, radius, pixels); }
        catch { Stop(); throw; }
    }
    async Task<IDictionary<string,object>?> Request(Func<Dictionary<string,object>,Task<ObjectPath>> invoke)
    {
        var token="pickerhub_"+Guid.NewGuid().ToString("N");
        var path="/org/freedesktop/portal/desktop/request/"+peer.TrimStart(':').Replace('.','_')+"/"+token;
        var result=new TaskCompletionSource<(uint code,IDictionary<string,object> data)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch=await connection.CreateProxy<IRequest>(Service,path).WatchResponseAsync(r=>result.TrySetResult(r),ex=>result.TrySetException(ex));
        var actual=await invoke(new(){{"handle_token",token}}).WaitAsync(TimeSpan.FromSeconds(10));
        if(actual.ToString()!=path)throw new InvalidOperationException(L.T("مسار طلب مشاركة الشاشة غير متوقع."));
        (uint code, IDictionary<string, object> data) response;
        try { response=await result.Task.WaitAsync(TimeSpan.FromMinutes(2)); }
        catch
        {
            try { await connection.CreateProxy<IRequest>(Service,path).CloseAsync().WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
            throw new IOException(L.T("انتهت مهلة قطّارة النظام. أعد المحاولة أو افتح إعدادات القطّارة."));
        }
        if(response.code==1)return null;
        if(response.code!=0)throw new InvalidOperationException(L.T("لم يُسمح بمشاركة الشاشة. أعد فتح القطّارة واسمح بمشاركة الشاشات المطلوبة."));
        return response.data;
    }
    async Task<bool> Open(Window owner)
    {
        // Fail before asking the user to share if the frame reader is unavailable.
        GstCapture.Initialize();
        peer=(await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5))).LocalName;
        try{await connection.CreateProxy<IPortalRegistry>(Service,"/org/freedesktop/portal/desktop").RegisterAsync("pickerhub",new Dictionary<string,object>());}
        catch(DBusException ex) when(ex.ErrorName is "org.freedesktop.DBus.Error.UnknownMethod" or "org.freedesktop.DBus.Error.UnknownInterface"){}
        var cast=connection.CreateProxy<IScreenCast>(Service,"/org/freedesktop/portal/desktop");
        var created=await Request(options=>{options["session_handle_token"]="session_"+Guid.NewGuid().ToString("N");return cast.CreateSessionAsync(options);});
        if(created==null)return false;
        var path=new ObjectPath((string)created["session_handle"]);
        session=connection.CreateProxy<ICastSession>(Service,path);
        void SessionEnded()
        {
            ended = true;
            SharingChanged?.Invoke();
        }
        closed=await session.WatchClosedAsync(_=>SessionEnded(),_=>SessionEnded());
        var selected=await Request(options=>{options["types"]=1u;options["multiple"]=true;options["cursor_mode"]=1u;return cast.SelectSourcesAsync(path,options);});
        if(selected==null)return false;
        var handle=owner.TryGetPlatformHandle();
        var parent=handle?.HandleDescriptor=="XID"?$"x11:{handle.Handle:x}":"";
        var started=await Request(options=>cast.StartAsync(path,parent,options));
        if(started==null)return false;
        if(!started.TryGetValue("streams",out var value) || value is not ValueTuple<uint,IDictionary<string,object>>[] streams || streams.Length==0)
            throw new InvalidOperationException(L.T("اختر شاشة واحدة على الأقل في نافذة المشاركة."));
        remote=await cast.OpenPipeWireRemoteAsync(path,new Dictionary<string,object>());
        try
        {
            foreach(var (node,properties) in streams)
            {
                var pos=properties.TryGetValue("position",out var p)&&p is ValueTuple<int,int> point?point:(0,0);
                sources.Add((new GstCapture(remote.DangerousGetHandle().ToInt32(),node),pos.Item1,pos.Item2));
            }
        }
        catch{Dispose();throw;}
        return true;
    }
    public void Dispose()
    {
        ended=true;closed?.Dispose();closed=null;
        foreach(var s in sources)s.capture.Dispose();sources.Clear();
        remote?.Dispose();remote=null;
        session=null;
        connection.Dispose();
    }
}

internal sealed class GstCapture : IDisposable
{
    const string Gst="libgstreamer-1.0.so.0",App="libgstapp-1.0.so.0";
    [DllImport(Gst)] static extern void gst_init(nint argc,nint argv);
    [DllImport(Gst)] static extern nint gst_parse_launch([MarshalAs(UnmanagedType.LPUTF8Str)]string pipeline,out nint error);
    [DllImport(Gst)] static extern nint gst_bin_get_by_name(nint bin,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [DllImport(Gst)] static extern int gst_element_set_state(nint element,int state);
    [DllImport(Gst)] static extern void gst_object_unref(nint obj);
    [DllImport(Gst)] static extern nint gst_sample_get_buffer(nint sample);
    [DllImport(Gst)] static extern nint gst_sample_get_caps(nint sample);
    [DllImport(Gst)] static extern void gst_sample_unref(nint sample);
    [DllImport(Gst)] static extern nint gst_caps_get_structure(nint caps,uint i);
    [DllImport(Gst)] static extern int gst_structure_get_int(nint structure,[MarshalAs(UnmanagedType.LPUTF8Str)]string field,out int value);
    [DllImport(App)] static extern nint gst_app_sink_try_pull_sample(nint sink,ulong timeout);
    [DllImport(Gst)] static extern int gst_buffer_map(nint buffer,out MapInfo map,int flags);
    [DllImport(Gst)] static extern void gst_buffer_unmap(nint buffer,ref MapInfo map);
    [DllImport("libglib-2.0.so.0")] static extern void g_error_free(nint error);
    [StructLayout(LayoutKind.Sequential)]
    struct MapInfo
    {
        public nint memory;public int flags;public nint data;public nuint size,maxsize;
        public nint user0,user1,user2,user3,reserved0,reserved1,reserved2,reserved3;
    }
    nint pipeline,sink;
    public static void Initialize()
    {
        try{gst_init(0,0);}
        catch(DllNotFoundException){throw new InvalidOperationException(L.T("ثبّت GStreamer وملحق PipeWire لالتقاط الشاشة دون وميض."));}
    }
    public GstCapture(int fd,uint node)
    {
        pipeline=gst_parse_launch($"pipewiresrc fd={fd} path={node} ! videoconvert ! video/x-raw,format=RGBA ! appsink name=sample max-buffers=1 drop=true sync=false",out var error);
        if(error!=0){g_error_free(error);Dispose();throw new InvalidOperationException(L.T("تعذّر تشغيل قارئ PipeWire. تحقق من تثبيت gstreamer1.0-pipewire وملحقات الفيديو."));}
        sink=gst_bin_get_by_name(pipeline,"sample");
        if(sink==0||gst_element_set_state(pipeline,4)==0){Dispose();throw new InvalidOperationException(L.T("تعذّر بدء تدفق الشاشة."));}
    }
    [DllImport(App)] static extern int gst_app_sink_is_eos(nint sink);
    nint latest;
    public void Drain()
    {
        if (latest != 0) { gst_sample_unref(latest); latest = 0; }
        var queued = gst_app_sink_try_pull_sample(sink, 0);
        if (queued != 0) gst_sample_unref(queued);
    }
    public bool Sample(PixelRect screen, PixelPoint point, int radius, byte[] pixels)
    {
        // Compositors can stop sending buffers when the desktop is still.
        // Keep the newest sample so a moving pointer can read another region.
        var next = gst_app_sink_try_pull_sample(sink, 0);
        if (next != 0)
        {
            if (latest != 0) gst_sample_unref(latest);
            latest = next;
        }
        if (gst_app_sink_is_eos(sink) != 0) throw new IOException(L.T("توقفت مشاركة الشاشة. أعد فتح القطّارة."));
        if (latest == 0) return false;
        var caps = gst_caps_get_structure(gst_sample_get_caps(latest), 0);
        if (gst_structure_get_int(caps, "width", out int w) == 0 || gst_structure_get_int(caps, "height", out int h) == 0
            || w <= 0 || h <= 0 || (long)w*h > 150_000_000) throw new IOException(L.T("أبعاد تدفق الشاشة غير صالحة."));
        var buffer = gst_sample_get_buffer(latest);
        if (gst_buffer_map(buffer, out var map, 1) == 0) throw new IOException(L.T("تعذّرت قراءة بكسلات الشاشة."));
        try
        {
            int stride = checked(w * 4);
            if (map.size < (nuint)((long)stride*h)) throw new IOException(L.T("إطار الشاشة غير مكتمل."));
            int side = 2*radius+1;
            for (int row = 0; row < side; row++)
            {
                int y = Math.Clamp((int)((long)(point.Y-screen.Y+row-radius)*h/screen.Height), 0, h-1);
                for (int col = 0; col < side; col++)
                {
                    int x = Math.Clamp((int)((long)(point.X-screen.X+col-radius)*w/screen.Width), 0, w-1);
                    int color = Marshal.ReadInt32(map.data, y*stride+x*4);
                    int i = (row*side+col)*4;
                    pixels[i] = (byte)color; pixels[i+1] = (byte)(color>>8); pixels[i+2] = (byte)(color>>16); pixels[i+3] = 255;
                }
            }
            return true;
        }
        finally { gst_buffer_unmap(buffer, ref map); }
    }
    public void Dispose()
    {
        if (latest != 0) { gst_sample_unref(latest); latest = 0; }
        if(pipeline!=0)gst_element_set_state(pipeline,1);
        if(sink!=0){gst_object_unref(sink);sink=0;}
        if(pipeline!=0){gst_object_unref(pipeline);pipeline=0;}
    }
}
