using Microsoft.Data.SqlClient;
using ARC.Data.Configuration;

namespace ARC.Data.Tests.Odos;

public sealed class OdosSqlConnectionOptionsTests
{
    private const string Server = "10.160.66.37";
    private const string Database = "BERGER_MOBILE_APP_DB";
    private const string User = "arc_reader";
    private const string Password = "not-a-real-password";

    private static OdosSqlConnectionOptions Valid() => new()
    {
        DBServerName = Server,
        DBName = Database,
        DBServerUserId = User,
        DBServerPassword = Password
    };

    [Fact]
    public void Configuration_MapsAppSettingsKeys()
    {
        var options = Valid();

        Assert.Equal(Server, options.DBServerName);
        Assert.Equal(Database, options.DBName);
        Assert.Equal(User, options.DBServerUserId);
        Assert.True(options.IsConfigured);
        Assert.Equal("AppSettings", OdosSqlConnectionOptions.SectionName);
    }

    [Fact]
    public void DatabaseName_StripsBackticksAndBrackets()
    {
        var options = Valid();
        options.DBName = "`BERGER_MOBILE_APP_DB`";

        Assert.Equal(Database, options.NormalizedDatabaseName);
        Assert.DoesNotContain("`", options.NormalizedDatabaseName);

        options.DBName = "[BERGER_MOBILE_APP_DB]";
        Assert.Equal(Database, options.NormalizedDatabaseName);
    }

    [Fact]
    public void ConnectionString_BuiltCentrally_WithSqlServerAuthentication()
    {
        var connectionString = Valid().BuildConnectionString();
        var builder = new SqlConnectionStringBuilder(connectionString);

        Assert.Equal(Server, builder.DataSource);
        Assert.Equal(Database, builder.InitialCatalog);
        Assert.Equal(User, builder.UserID);
        Assert.False(builder.IntegratedSecurity);
        Assert.Equal(15, builder.ConnectTimeout);
    }

    [Fact]
    public void ConnectionString_NeverContainsBackticksInDatabaseName()
    {
        var options = Valid();
        options.DBName = "`BERGER_MOBILE_APP_DB`";

        var builder = new SqlConnectionStringBuilder(options.BuildConnectionString());
        Assert.Equal(Database, builder.InitialCatalog);
    }

    [Fact]
    public void Validate_RejectsWrongDatabase()
    {
        var options = Valid();
        options.DBName = "SOME_OTHER_DB";

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains(OdosSqlConnectionOptions.ApprovedDatabaseName, ex.Message);
    }

    [Fact]
    public void Validate_RequiresPasswordFromOutsideCommittedSource()
    {
        var options = Valid();
        options.DBServerPassword = "";

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("user-secrets", ex.Message);
        Assert.DoesNotContain(Password, ex.Message);
    }

    [Fact]
    public void SafeDescriptor_DoesNotContainPassword()
    {
        var options = Valid();

        Assert.DoesNotContain(Password, options.SafeDescriptor);
        Assert.DoesNotContain(options.DBServerPassword, options.SafeDescriptor);
        Assert.Equal($"{Server}/{Database}", options.SafeDescriptor);
    }

    [Fact]
    public void IsConfigured_FalseWhenPasswordMissing()
    {
        var options = Valid();
        options.DBServerPassword = "";

        Assert.False(options.IsConfigured);
    }
}
