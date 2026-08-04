using NeonNightSDK.Ui;

namespace NeonNightSDK.Settings.Options
{
    // A heading that splits a long page into groups. Not a control — no value, no key.
    //
    // Worth having as an option rather than a helper on the page: it keeps the page a single
    // ordered list, so a section always renders exactly where it was declared, between the two
    // options it separates.
    public sealed class SectionOption : SettingOption
    {
        public SectionOption(string label, string description)
            : base(null, label, description)
        {
        }

        public override void Render(UiBuilder body)
        {
            body.Spacer(10f)
                .Heading(Label)
                .Separator();

            if (!string.IsNullOrEmpty(Description))
                body.Muted(Description);
        }
    }
}
