# Third-party notices

MicToggle is distributed with the following third-party components. These
components are not relicensed under the MicToggle MIT License.

| Component | Version | License | Included notice |
| --- | --- | --- | --- |
| Microsoft.Web.WebView2 | 1.0.4078.44 | BSD 3-Clause | `third-party/Microsoft.Web.WebView2-LICENSE.txt`, `third-party/Microsoft.Web.WebView2-NOTICE.txt` |
| NAudio.Core | 2.3.0 | MIT | `third-party/NAudio-LICENSE.txt` |
| NAudio.Wasapi | 2.3.0 | MIT | `third-party/NAudio-LICENSE.txt` |
| .NET application host | 8.0.29 | MIT and applicable third-party notices | `third-party/dotnet-LICENSE.txt`, `third-party/dotnet-THIRD-PARTY-NOTICES.txt` |

The framework-dependent release does not bundle the .NET Desktop Runtime or the
WebView2 Evergreen Runtime. Users install those prerequisites separately under
Microsoft's terms.

The exact license and notice files in `third-party` are copied into every
release archive by `scripts/publish.ps1`.
