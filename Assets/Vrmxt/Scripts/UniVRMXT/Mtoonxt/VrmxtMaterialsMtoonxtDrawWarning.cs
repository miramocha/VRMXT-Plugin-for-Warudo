namespace UniVRMXT.Mtoonxt
{
    /// <summary>
    /// One authoring warning when a stencil writer would draw after its clip reader.
    /// </summary>
    public readonly struct VrmxtMaterialsMtoonxtDrawWarning
    {
        public readonly string Headline;
        public readonly string Detail;

        public VrmxtMaterialsMtoonxtDrawWarning(string headline, string detail)
        {
            Headline = headline;
            Detail = detail;
        }
    }
}
