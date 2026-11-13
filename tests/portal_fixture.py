import dbus,dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib
DBusGMainLoop(set_as_default=True)
bus=dbus.SessionBus(); name=dbus.service.BusName('org.freedesktop.portal.Desktop',bus)
class Request(dbus.service.Object):
 @dbus.service.signal('org.freedesktop.portal.Request',signature='ua{sv}')
 def Response(self,c,res):pass
class Portal(dbus.service.Object):
 @dbus.service.method('org.freedesktop.portal.Screenshot',in_signature='sa{sv}',out_signature='o',sender_keyword='sender')
 def PickColor(self,parent,options,sender):
  path='/org/freedesktop/portal/desktop/request/'+sender[1:].replace('.','_')+'/'+options['handle_token']
  req=Request(bus,path)
  def respond():
   req.Response(dbus.UInt32(0),{'color':dbus.Struct([dbus.Double(0.2),dbus.Double(0.4),dbus.Double(0.6)],signature='ddd',variant_level=1)});req.remove_from_connection();return False
  GLib.timeout_add(50,respond);return dbus.ObjectPath(path)
p=Portal(bus,'/org/freedesktop/portal/desktop');GLib.MainLoop().run()
