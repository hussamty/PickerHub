//! Wayland: the GNOME Color Picker extension when it is active (it shows its own lens), otherwise
//! the XDG desktop portal's `PickColor`. PickerHub receives only the chosen color.
use crate::{CaptureError, Rgb};
use std::collections::HashMap;
use std::time::Duration;
use zbus::zvariant::OwnedValue;

const EXTENSION: &str = "org.gnome.Shell.Extensions.ColorPicker";

fn unavailable() -> CaptureError {
    CaptureError::new("portal_unavailable", "قطّارة النظام غير متاحة. تحقق من تثبيت بوابة سطح المكتب الخاصة ببيئتك.")
}
fn timeout() -> CaptureError {
    CaptureError::new("portal_timeout", "انتهت مهلة قطّارة النظام. أعد المحاولة أو افتح إعدادات القطّارة.")
}
fn invalid() -> CaptureError {
    CaptureError::new("portal_invalid", "أعادت قطّارة النظام لوناً غير صالح.")
}

async fn extension(conn: &zbus::Connection) -> Option<Result<Option<Rgb>, CaptureError>> {
    let dbus = zbus::fdo::DBusProxy::new(conn).await.ok()?;
    if !dbus.name_has_owner(EXTENSION.try_into().ok()?).await.unwrap_or(false) {
        return None;
    }
    let reply = tokio::time::timeout(
        Duration::from_secs(120),
        conn.call_method(Some(EXTENSION), "/org/gnome/Shell/Extensions/ColorPicker", Some(EXTENSION), "Pick", &()),
    )
    .await;
    Some(match reply {
        Err(_) => Err(timeout()),
        // G_IO_ERROR_CANCELLED from the extension.
        Ok(Err(zbus::Error::MethodError(name, _, _))) if name.as_str().ends_with("Code19") => Ok(None),
        // Disabled between the check and the call: fall back to the portal.
        Ok(Err(_)) => return None,
        Ok(Ok(msg)) => (|| {
            let map: HashMap<String, OwnedValue> = msg.body().deserialize().map_err(|_| invalid())?;
            let (r, g, b): (f64, f64, f64) = map.get("color").ok_or_else(invalid)?.try_clone().map_err(|_| invalid())?.try_into().map_err(|_| invalid())?;
            Rgb::from_unit(r, g, b).map(Some)
        })(),
    })
}

pub async fn pick() -> Result<Option<Rgb>, CaptureError> {
    if let Ok(conn) = zbus::Connection::session().await
        && let Some(result) = extension(&conn).await
    {
        return result;
    }
    let request = tokio::time::timeout(Duration::from_secs(10), ashpd::desktop::Color::pick().send())
        .await
        .map_err(|_| timeout())?
        .map_err(|e| unavailable().with(e))?;
    match tokio::time::timeout(Duration::from_secs(120), async { request.response() }).await.map_err(|_| timeout())? {
        Ok(color) => Rgb::from_unit(color.red(), color.green(), color.blue()).map(Some),
        Err(ashpd::Error::Response(ashpd::desktop::ResponseError::Cancelled)) => Ok(None),
        Err(e) => Err(CaptureError::new("portal_failed", "تعذّر إتمام اختيار اللون في قطّارة النظام.").with(e)),
    }
}
