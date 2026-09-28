using TaskRunner.Bilibili.Api;
using Xunit;

namespace TaskRunner.Tests;

public class BilibiliWbiSignerTests
{
    // 标准 wbi 向量（与 Python 参考实现 SocialSisterYi 混淆表逐位核对一致）。
    private const string ImgUrl = "https://i0.hdslb.com/bfs/wbi/7cd084941338484aae1ad9425b84077c.png";
    private const string SubUrl = "https://i0.hdslb.com/bfs/wbi/4932caff0ff746eab6f01bf08b70ac45.png";

    [Fact]
    public void Sign_标准向量_生成与参考实现一致的签名()
    {
        var query = BilibiliWbiSigner.Sign(
            new Dictionary<string, string?>
            {
                ["foo"] = "114",
                ["bar"] = "514",
                ["baz"] = "1919810"
            },
            ImgUrl,
            SubUrl,
            wtsOverride: 1702204169);

        Assert.Equal(
            "bar=514&baz=1919810&foo=114&wts=1702204169&w_rid=6149fdadf571698ca7e6a567265cd0ee",
            query);
    }

    [Fact]
    public void Sign_缺少钥匙_抛出明确异常()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BilibiliWbiSigner.Sign(new Dictionary<string, string?> { ["a"] = "1" }, null, SubUrl));
    }
}
