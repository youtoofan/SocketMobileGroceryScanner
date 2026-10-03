using Android.App;
using Android.Content;
using Microsoft.Identity.Client;

namespace SocketMobileGroceryScanner;

[Activity(Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "msalcb8a627b-0974-40cd-8ced-aa5c8e6659c5",
    DataHost = "auth")]
public class MsalActivity : BrowserTabActivity
{
}
