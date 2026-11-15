"""Isolated Wayland shortcut consent, activation and window-return regression test."""
import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib
import json
import os
import subprocess
import tempfile
from pathlib import Path
DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus()
name = dbus.service.BusName('org.freedesktop.portal.Desktop', bus)
loop = GLib.MainLoop()
class Request(dbus.service.Object):
    @dbus.service.signal('org.freedesktop.portal.Request', signature='ua{sv}')
    def Response(self, code, values): pass
    @dbus.service.method('org.freedesktop.portal.Request')
    def Close(self): pass
class Portal(dbus.service.Object):
    binds = 0
    picks = 0
    session = None
    @dbus.service.method('org.freedesktop.host.portal.Registry', in_signature='sa{sv}', out_signature='')
    def Register(self, appid, options): assert appid == 'pickerhub'
    def respond(self, sender, options, values):
        path = '/org/freedesktop/portal/desktop/request/' + sender[1:].replace('.','_') + '/' + options['handle_token']
        request = Request(bus, path)
        def send():
            request.Response(0, values); request.remove_from_connection(); return False
        GLib.timeout_add(80, send)
        return dbus.ObjectPath(path)
    @dbus.service.method('org.freedesktop.portal.GlobalShortcuts', in_signature='a{sv}', out_signature='o', sender_keyword='sender')
    def CreateSession(self, options, sender):
        self.session = '/org/freedesktop/portal/desktop/session/' + sender[1:].replace('.','_') + '/' + options['session_handle_token']
        return self.respond(sender, options, {'session_handle': dbus.String(self.session)})
    @dbus.service.method('org.freedesktop.portal.GlobalShortcuts', in_signature='oa(sa{sv})sa{sv}', out_signature='o', sender_keyword='sender')
    def BindShortcuts(self, session, shortcuts, parent, options, sender):
        assert session == self.session and shortcuts[0][0] == 'pick' and parent.startswith('x11:')
        self.binds += 1
        value = dbus.Array([dbus.Struct(('pick', dbus.Dictionary({'trigger_description':'Ctrl+Shift+P'}, signature='sv')), signature='sa{sv}')], signature='(sa{sv})')
        return self.respond(sender, options, {'shortcuts': value})
    @dbus.service.signal('org.freedesktop.portal.GlobalShortcuts', signature='osta{sv}')
    def Activated(self, session, shortcut, timestamp, options): pass
    @dbus.service.method('org.freedesktop.portal.Screenshot', in_signature='sa{sv}', out_signature='o', sender_keyword='sender')
    def PickColor(self, parent, options, sender):
        self.picks += 1
        return self.respond(sender, options, {'color':dbus.Struct(tuple(dbus.Double(v/255) for v in (49,117,179)), signature='ddd')})
portal = Portal(bus, '/org/freedesktop/portal/desktop')
ROOT = Path(__file__).resolve().parents[1]
passed = False
with tempfile.TemporaryDirectory() as temporary:
    folder = Path(temporary)/'PickerHub'; folder.mkdir()
    workspace = folder/'workspace.json'
    workspace.write_text(json.dumps({'version':1,'color':'#87CEEB','limit':32,'theme':'dark','history':[],'palettes':[{'id':'test','name':'Test','colors':[]}],'gradients':[],'globalShortcut':True}))
    env = dict(os.environ, XDG_DATA_HOME=temporary, XDG_SESSION_TYPE='wayland', WAYLAND_DISPLAY='')
    process = subprocess.Popen([ROOT/'dist/native/linux-x64/PickerHub/PickerHub'],env=env)
    phase = 0
    def tick():
        global phase, passed
        if process.poll() is not None: loop.quit(); return False
        if portal.binds and phase == 0:
            portal.Activated(portal.session,'pick',0,{}); phase = 1
        elif phase == 1 and portal.picks == 1 and json.loads(workspace.read_text())['history'] == ['#3175B3']:
            phase = 2
            GLib.timeout_add(700, lambda: (portal.Activated(portal.session,'pick',1,{}), False)[1])
        elif phase == 2 and portal.picks == 2:
            phase = 3
            def finish():
                global passed
                passed = portal.binds == 1 and json.loads(workspace.read_text())['history'] == ['#3175B3']
                loop.quit(); return False
            GLib.timeout_add(700, finish)
        return True
    GLib.timeout_add(150, tick)
    GLib.timeout_add_seconds(20, lambda: (loop.quit(),False)[1])
    try: loop.run()
    finally: process.terminate(); process.wait(timeout=5)
assert passed, f'Portal shortcut failed: binds={portal.binds}, picks={portal.picks}, phase={phase}'
print('PASS: Wayland shortcut binds once, triggers twice, and survives picker window restoration')
