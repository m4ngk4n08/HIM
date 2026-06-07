using System;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Events;
using System.Reflection;
using System.Linq;

public class Probe {
    public static void Main() {
        try {
            var type = typeof(SshAuthenticatingEventArgs);
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                Console.WriteLine($"Property: {prop.Name} ({prop.PropertyType.FullName})");
            }
        } catch (Exception ex) {
            Console.WriteLine(ex.Message);
        }
    }
}
