"""Isolated ScreenCast portal. Publishes generated pixels through a test PipeWire server."""
import dbus
import dbus.service
import json
import os
import socket
import threading
import subprocess
import gi
gi.require_version("Gst","1.0")
from gi.repository import GLib,Gst
from dbus.mainloop.glib import DBusGMainLoop

DBusGMainLoop(set_as_default=True)
Gst.init(None)
bus=dbus.SessionBus()
name=dbus.service.BusName("org.freedesktop.portal.Desktop",bus)
pipeline=Gst.parse_launch('videotestsrc name=generator is-live=true pattern=solid-color foreground-color=0xff336699 ! video/x-raw,format=RGBA,width=1400,height=900,framerate=10/1 ! pipewiresink mode=provide stream-properties="props,media.class=Video/Source,node.name=PickerHubFixture"')
pipeline.set_state(Gst.State.PLAYING)
node_id=None
def find_node():
    global node_id
    nodes=json.loads(subprocess.check_output(["pw-dump","-r","pipewire-0-manager"],timeout=6))
    node_id=next(n["id"] for n in nodes if n.get("info",{}).get("props",{}).get("node.name")=="PickerHubFixture")
threading.Thread(target=find_node,daemon=True).start()

class Request(dbus.service.Object):
    @dbus.service.signal("org.freedesktop.portal.Request",signature="ua{sv}")
    def Response(self,code,result):pass

class Session(dbus.service.Object):
    @dbus.service.method("org.freedesktop.portal.Session",in_signature="",out_signature="")
    def Close(self):self.Closed({})
    @dbus.service.signal("org.freedesktop.portal.Session",signature="a{sv}")
    def Closed(self,result):pass

class Portal(dbus.service.Object):
    registered={}
    starts=0
    @dbus.service.method("org.freedesktop.host.portal.Registry",in_signature="sa{sv}",out_signature="",sender_keyword="sender")
    def Register(self,app_id,options,sender):
        assert app_id=="pickerhub"
        self.registered[sender]=app_id
    def reply(self,options,sender,data):
        path="/org/freedesktop/portal/desktop/request/"+sender[1:].replace(".","_")+"/"+options["handle_token"]
        req=Request(bus,path)
        def respond():
            code=int(os.environ.get("PICKERHUB_PORTAL_RESULT","0"))
            req.Response(dbus.UInt32(code),data if code==0 else {})
            req.remove_from_connection()
            return False
        GLib.timeout_add(250,respond)
        return dbus.ObjectPath(path)
    @dbus.service.method("org.freedesktop.portal.Screenshot", in_signature="sa{sv}", out_signature="o", sender_keyword="sender")
    def PickColor(self, parent, options, sender):
        assert parent.startswith("x11:") and self.starts == 0
        rgb = dbus.Struct(tuple(dbus.Double(v / 255) for v in (49, 117, 179)), signature="ddd", variant_level=1)
        return self.reply(options, sender, {"color": rgb})
    @dbus.service.method("org.freedesktop.portal.ScreenCast",in_signature="a{sv}",out_signature="o",sender_keyword="sender")
    def CreateSession(self,options,sender):
        assert self.registered.get(sender)=="pickerhub"
        path="/org/freedesktop/portal/desktop/session/"+sender[1:].replace(".","_")+"/"+options["session_handle_token"]
        self.session=Session(bus,path)
        return self.reply(options,sender,{"session_handle":dbus.String(path,variant_level=1)})
    @dbus.service.method("org.freedesktop.portal.ScreenCast",in_signature="oa{sv}",out_signature="o",sender_keyword="sender")
    def SelectSources(self,session,options,sender):
        assert options["multiple"] and options["types"]==1
        return self.reply(options,sender,{})
    @dbus.service.method("org.freedesktop.portal.ScreenCast",in_signature="osa{sv}",out_signature="o",sender_keyword="sender")
    def Start(self,session,parent,options,sender):
        self.starts+=1
        assert parent.startswith("x11:")
        wid="0x"+parent[4:]
        assert "Map State: IsViewable" in subprocess.check_output(["xwininfo","-id",wid],text=True)
        assert "= 0" in subprocess.check_output(["xprop","-id",wid,"_NET_WM_WINDOW_OPACITY"],text=True)
        assert node_id is not None,"Test video node is not ready"
        node=node_id
        props=dbus.Dictionary({"position":dbus.Struct((dbus.Int32(0),dbus.Int32(0)),signature="ii",variant_level=1)},signature="sv")
        streams=dbus.Array([dbus.Struct((dbus.UInt32(node),props),signature="ua{sv}")],signature="(ua{sv})",variant_level=1)
        return self.reply(options,sender,{"streams":streams})
    @dbus.service.method("org.freedesktop.portal.ScreenCast",in_signature="oa{sv}",out_signature="h")
    def OpenPipeWireRemote(self,session,options):
        sock=socket.socket(socket.AF_UNIX)
        sock.connect(os.path.join(os.environ["XDG_RUNTIME_DIR"],"pipewire-0-manager"))
        fd=dbus.types.UnixFd(sock.fileno())
        sock.close()
        return fd
    @dbus.service.method("org.pickerhub.Test",in_signature="u",out_signature="u")
    def Color(self,rgb):
        pipeline.get_by_name("generator").set_property("foreground-color",0xff000000|rgb)
        return self.starts

portal=Portal(bus,"/org/freedesktop/portal/desktop")
try:GLib.MainLoop().run()
finally:pipeline.set_state(Gst.State.NULL)
