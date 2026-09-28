namespace TaskRunner.Bilibili.Models;

/// <summary>统一的视频目标，来源可为关注 UP 主或热门榜单。</summary>
public sealed record VideoTarget(long Aid, string Bvid, long Cid, string Title, string Source);
