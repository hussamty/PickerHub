use serde::Serialize;
use std::fmt;

/// A failure the interface can show. `message` is the Arabic source text, which is also the key of
/// the English catalog (`Localization.en.json`), so the interface translates it like any other label.
#[derive(Debug)]
pub enum StoreError {
    Invalid { code: &'static str, message: &'static str },
    NewerVersion,
    Locked,
    Io(std::io::Error),
}

impl StoreError {
    pub fn invalid_backup() -> Self {
        Self::Invalid { code: "invalid_backup", message: "اختر نسخة PickerHub احتياطية صالحة." }
    }
    pub fn invalid_library() -> Self {
        Self::Invalid { code: "invalid_library", message: "بيانات المكتبة غير صالحة." }
    }
    pub fn invalid(code: &'static str, message: &'static str) -> Self {
        Self::Invalid { code, message }
    }
    pub fn code(&self) -> &'static str {
        match self {
            Self::Invalid { code, .. } => code,
            Self::NewerVersion => "newer_version",
            Self::Locked => "locked",
            Self::Io(_) => "io",
        }
    }
    pub fn message(&self) -> String {
        match self {
            Self::Invalid { message, .. } => (*message).to_string(),
            Self::NewerVersion => "هذه المكتبة تحتاج إلى إصدار أحدث من PickerHub.".into(),
            Self::Locked => "PickerHub مفتوح بالفعل. أغلق النسخة الأخرى ثم أعد المحاولة.".into(),
            // The OS text can carry a path; the support report never includes it, the interface may.
            Self::Io(e) => format!("تعذّر حفظ المكتبة: {e}"),
        }
    }
}

impl fmt::Display for StoreError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}: {}", self.code(), self.message())
    }
}
impl std::error::Error for StoreError {}
impl From<std::io::Error> for StoreError {
    fn from(e: std::io::Error) -> Self {
        Self::Io(e)
    }
}

/// What crosses to the interface.
#[derive(Serialize, Debug, Clone)]
pub struct ErrorBody {
    pub code: String,
    pub message: String,
}
impl From<&StoreError> for ErrorBody {
    fn from(e: &StoreError) -> Self {
        Self { code: e.code().into(), message: e.message() }
    }
}
