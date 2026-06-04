namespace HermesOnDisk;

public abstract class Program
{
    public static void Main(string[] args)
    {
        HermesOnDisk.Root = new DirectoryInfo(AppContext.BaseDirectory);
        HermesOnDisk.String[0] = "MyName";
        HermesOnDisk.Float[0] = 12;
        HermesOnDisk.Boolean[0] = true;
        HermesOnDisk.Store();
    }
}
