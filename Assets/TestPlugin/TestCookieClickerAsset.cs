using Warudo.Core.Attributes;
using Warudo.Core.Scenes;

/// <summary>
/// Minimal Cookie Clicker clone (no particle prefab) for UMod smoke test.
/// </summary>
[AssetType(
    Id = "0e9439d4-b238-47b3-89d1-84318623a95e",
    Title = "Test Cookie Clicker",
    Category = "CATEGORY_DEBUG"
)]
public class TestCookieClickerAsset : Asset
{
    [Markdown]
    public string Status = "You don't have any cookies.";

    [DataInput]
    [IntegerSlider(1, 10)]
    [Description("Increase me to get more cookies each time!")]
    public int Multiplier = 1;

    private int _count;

    [Trigger]
    public void GimmeCookie()
    {
        _count += Multiplier;
        SetDataInput(
            nameof(Status),
            "You have " + _count + " cookie(s).",
            broadcast: true);
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        SetActive(true);
    }
}
