using System.Windows.Forms;

namespace CuaDriver.Win.HardCases;

internal sealed class RdpActiveXControl(string clsid) : AxHost(clsid)
{
    public object OcxObject => GetOcx() ?? throw new InvalidOperationException("RDP ActiveX control did not expose an OCX object.");
}
