using System.ComponentModel;

namespace DcsAutopilot;

public abstract class FlightControllerBase : INotifyPropertyChanged
{
    public abstract string Name { get; set; }
    /// <summary>
    ///     Disabled controllers receive no callbacks, and are as good as completely removed from the list of controllers.</summary>
    public bool Enabled
    {
        get { return _enabled; }
        set
        {
            _enabled = value;
            if (_enabled && Dcs?.IsRunning == true) Reset();
            PropertyChanged?.Invoke(this, new(nameof(Enabled)));
        }
    }
    private bool _enabled = false;
    public string Status { get { return _status; } protected set { _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); } }
    private string _status = "";
    public DcsController Dcs { get; set; }
    /// <summary>Called on Dcs.Start, on setting <see cref="Enabled"/>=true, and also before every <see cref="NewSession"/>.</summary>
    public virtual void Reset() { }
    public virtual void NewSession(BulkData bulk) { }
    /// <summary>
    ///     Called only for enabled controllers every time a new data frame is received from DCS.</summary>
    /// <param name="frame">
    ///     Latest data frame. Equal to <see cref="DcsController.LastFrame"/>.</param>
    /// <returns>
    ///     Desired inputs. Null values for things that don't need to be controlled, or null if nothing needs to be
    ///     controlled.</returns>
    public virtual ControlData ProcessFrame(FrameData frame) { return null; }
    public virtual void ProcessBulkUpdate(BulkData bulk) { }
    public virtual void HandleSignal(string signal) { }
    public virtual bool HandleKey(KeyEventArgs e) { return false; }
    public event PropertyChangedEventHandler PropertyChanged;
}
