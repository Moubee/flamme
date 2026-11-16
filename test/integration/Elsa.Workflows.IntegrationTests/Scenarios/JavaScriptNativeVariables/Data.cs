using System.Dynamic;

namespace Elsa.Workflows.IntegrationTests.Scenarios.JavaScriptNativeVariables;

public class Data
{
    public static ExpandoObject CreateCustomerModel()
    {
        var customer = new ExpandoObject();
        IDictionary<string, object?> properties = customer;
        properties["Name"] = "John Doe";
        properties["Age"] = 42;
        properties["Orders"] = new[]
        {
            new
            {
                Id = 1,
                Product = "Apple",
                Price = 10
            },
            new 
            {
                Id = 3,
                Product = "Orange",
                Price = 30
            },
            new
            {
                Id = 2,
                Product = "Banana",
                Price = 20
            }
        };
        return customer;
    }
}
