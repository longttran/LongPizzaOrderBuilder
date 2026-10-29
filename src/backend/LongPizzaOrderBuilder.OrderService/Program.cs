namespace LongPizzaOrderBuilder.OrderService;

/// <summary>
/// The order service's entry point. It is deliberately not called <c>Program</c> (which is what top-level statements would generate):
/// the order builder's API already has a <c>Program</c> type, the test project references both back ends, and two types with the
/// same name in two referenced assemblies is a compile error (CS0433).
/// </summary>
public static class OrderServiceProgram
{
    public static void Main(string[] args) => OrderServiceHost.Build(args).Run();
}
