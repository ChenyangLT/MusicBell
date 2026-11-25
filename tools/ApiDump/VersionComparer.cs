sealed class VersionComparer : IComparer<string> {
  public static readonly VersionComparer Instance = new();
  public int Compare(string? a, string? b) {
    var pa = (a ?? "").Split('.'); var pb = (b ?? "").Split('.');
    for (int i=0;i<Math.Max(pa.Length,pb.Length);i++) {
      int x = i<pa.Length && int.TryParse(pa[i], out var xv) ? xv : 0;
      int y = i<pb.Length && int.TryParse(pb[i], out var yv) ? yv : 0;
      if (x!=y) return x.CompareTo(y);
    }
    return 0;
  }
}
