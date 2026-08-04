using NeonNightSDK.Ui;

namespace NeonNightSDK.Settings
{
    // One row in a settings page.
    //
    // This is the Strategy the whole kit turns on: the window doesn't know what a toggle or a
    // slider is, it just calls Render() on whatever is in the list, and the store doesn't know
    // either — it calls Serialize()/Deserialize(). Adding a new kind of option (a colour
    // picker, a file path) means writing one class here and touching nothing else. The
    // alternative — an OptionType enum plus a switch in the renderer and another in the
    // store — is the shape this deliberately avoids.
    public interface ISettingOption
    {
        // Stable id used as the JSON key. Null or empty means "not persisted", which is what
        // buttons and headings are.
        string Key { get; }

        string Label { get; }

        // Draws this option into the page body.
        void Render(UiBuilder body);

        // Current value as a string, or null when there's nothing to persist.
        string Serialize();

        // Applies a previously serialized value. Must tolerate garbage: the file is editable
        // by hand and survives mod updates that change an option's type.
        void Deserialize(string raw);

        // Back to the value the mod shipped with.
        void ResetToDefault();
    }
}
