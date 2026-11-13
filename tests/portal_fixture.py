"""Screenshot portal fixture for an isolated D-Bus session and Xvfb desktop."""
import dbus
import dbus.service
import struct
import zlib
import tempfile
import os
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib

DBusGMainLoop(set_as_default=True)
bus=dbus.SessionBus()
name=dbus.service.BusName('org.freedesktop.portal.Desktop',bus)

def png():
    def chunk(kind, data):
        return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
    raw=(b'\0'+bytes([51,102,153])*1400)*900
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1400,900,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b'')

class Request(dbus.service.Object):
    @dbus.service.signal('org.freedesktop.portal.Request',signature='ua{sv}')
    def Response(self,code,result):
        pass

class Portal(dbus.service.Object):
    @dbus.service.method('org.freedesktop.portal.Screenshot',in_signature='sa{sv}',out_signature='o',sender_keyword='sender')
    def Screenshot(self,parent,options,sender):
        path='/org/freedesktop/portal/desktop/request/'+sender[1:].replace('.','_')+'/'+options['handle_token']
        request=Request(bus,path)
        def respond():
            code=int(os.environ.get("PICKERHUB_PORTAL_RESULT","0"))
            if code:
                request.Response(dbus.UInt32(code),{})
                request.remove_from_connection()
                return False
            with tempfile.NamedTemporaryFile(suffix='.png',delete=False) as file:
                file.write(png())
                uri='file://'+file.name
            request.Response(dbus.UInt32(0),{'uri':dbus.String(uri,variant_level=1)})
            request.remove_from_connection()
            return False
        GLib.timeout_add(250,respond)
        return dbus.ObjectPath(path)

portal=Portal(bus,'/org/freedesktop/portal/desktop')
GLib.MainLoop().run()
