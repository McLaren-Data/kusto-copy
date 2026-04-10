using System.Data;
using KustoCopyConsole.Kusto;
using KustoCopyConsole.Kusto.Data;

namespace KustoCopyTest;

/// <summary>
/// Tests for safe DBNull handling when materialising ProtoBlock rows from IDataReader.
/// Covers the fix for InvalidCastException when CreatedOn is DBNull (leftouter lookup
/// can produce null for CreatedOn even when source extents have MinCreatedOn/MaxCreatedOn).
/// </summary>
public class DbQueryClientTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static DataTable CreateProtoBlockTable()
    {
        var table = new DataTable();
        table.Columns.Add("RowCount", typeof(long));
        table.Columns.Add("MinIngestionTime", typeof(string));
        table.Columns.Add("MaxIngestionTime", typeof(string));
        table.Columns.Add("CreatedOn", typeof(DateTime));
        return table;
    }

    /// <summary>
    /// Mimics the extraction lambda used inside GetProtoBlocksAsync.
    /// </summary>
    private static ProtoBlock ExtractProtoBlock(IDataReader r) =>
        new ProtoBlock(
            (long)r["RowCount"],
            (string)r["MinIngestionTime"],
            (string)r["MaxIngestionTime"],
            r["CreatedOn"] is DBNull ? null : (DateTime?)r["CreatedOn"]);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void ExtractProtoBlock_WhenCreatedOnIsDbNull_ReturnsNullCreationTime()
    {
        // Arrange – simulate a leftouter-lookup row where CreatedOn has no match
        var table = CreateProtoBlockTable();
        table.Rows.Add(100L, "2024-01-01T00:00:00Z", "2024-01-02T00:00:00Z", DBNull.Value);

        using var reader = table.CreateDataReader();

        // Act
        var blocks = reader.ToEnumerable(ExtractProtoBlock).ToList();

        // Assert – must not throw; CreationTime must be null
        Assert.Single(blocks);
        Assert.Null(blocks[0].CreationTime);
    }

    [Fact]
    public void ExtractProtoBlock_WhenCreatedOnIsValid_ReturnsCorrectCreationTime()
    {
        // Arrange
        var expectedCreatedOn = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var table = CreateProtoBlockTable();
        table.Rows.Add(200L, "2024-06-01T00:00:00Z", "2024-06-02T00:00:00Z", expectedCreatedOn);

        using var reader = table.CreateDataReader();

        // Act
        var blocks = reader.ToEnumerable(ExtractProtoBlock).ToList();

        // Assert
        Assert.Single(blocks);
        Assert.Equal(expectedCreatedOn, blocks[0].CreationTime);
    }

    [Fact]
    public void ExtractProtoBlock_MultipleRows_MixedNullAndNonNullCreatedOn_HandlesCorrectly()
    {
        // Arrange – mix of rows: some with CreatedOn, some without
        var createdOn = new DateTime(2024, 3, 10, 8, 0, 0, DateTimeKind.Utc);
        var table = CreateProtoBlockTable();
        table.Rows.Add(50L, "2024-03-01T00:00:00Z", "2024-03-02T00:00:00Z", DBNull.Value);
        table.Rows.Add(75L, "2024-03-03T00:00:00Z", "2024-03-04T00:00:00Z", createdOn);
        table.Rows.Add(30L, "2024-03-05T00:00:00Z", "2024-03-06T00:00:00Z", DBNull.Value);

        using var reader = table.CreateDataReader();

        // Act
        var blocks = reader.ToEnumerable(ExtractProtoBlock).ToList();

        // Assert
        Assert.Equal(3, blocks.Count);
        Assert.Null(blocks[0].CreationTime);
        Assert.Equal(createdOn, blocks[1].CreationTime);
        Assert.Null(blocks[2].CreationTime);
    }
}
