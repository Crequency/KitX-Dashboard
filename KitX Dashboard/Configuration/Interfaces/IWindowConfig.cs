using Common.BasicHelper.Graphics.Screen;

namespace KitX.Dashboard.Configuration.Interfaces;

public interface IWindowConfig
{
    public Resolution Size { get; set; }

    public Distances Location { get; set; }
}
