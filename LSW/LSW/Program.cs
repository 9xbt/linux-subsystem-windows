using System.Diagnostics;

namespace MyApp
{
    internal class Program
    {
        const string QEMUFlags = "-m 2G -M q35 -drive file=linux.qcow2,format=qcow2 -accel whpx,kernel-irqchip=off -net nic -net user,hostfwd=tcp::2222-:22 -device virtio-gpu";
        const string SSHFlags = "-p 2222 -X";

        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("lsw: no parameters provided");
                Console.WriteLine("Usage: lsw [install|graphical] [username]");
                return;
            }

            switch (args[0])
            {
                case "install":
                    if (File.Exists("linux.qcow2"))
                    {
                        Console.WriteLine("lsw: linux.qcow2: file exists");
                        Console.Write("Do you want to reinstall? [y/N] ");

                        if (char.ToLower(Console.ReadKey().KeyChar) != 'y')
                        {
                            Console.WriteLine("\nlsw: installation failed: cancelled by user");
                            return;
                        }
                    }

                    Console.WriteLine("lsw: creating image");
                    Process.Start("qemu-img", "create -f qcow2 linux.qcow2 10G").WaitForExit();
                    await GetInstallerAsync("https://cdimage.debian.org/debian-cd/current/amd64/iso-cd/debian-12.9.0-amd64-netinst.iso", "installer.iso");
                    Process.Start("qemu-system-x86_64", QEMUFlags + " -cdrom installer.iso").WaitForExit();
                    return;
                case "graphical":
                    Process.Start("qemu-system-x86_64", QEMUFlags).WaitForExit();
                    return;
                default:
                    Process.Start("qemu-system-x86_64", QEMUFlags + " -vnc :0");
                    Console.WriteLine("lsw: trying to connect to vm...");

                    var exit = 255;
                    while (exit == 255)
                    {
                        using (var proc = Process.Start("ssh", $"{SSHFlags} {args[0]}@localhost"))
                        {
                            proc.WaitForExit();
                            exit = proc.ExitCode;
                        }
                        await Task.Delay(1000);
                    }
                    return;
            }
        }

        static async Task GetInstallerAsync(string url, string file)
        {
            if (File.Exists(file))
            {
                Console.WriteLine("lsw: installer already downloaded, skipping download");
                return;
            }

            using (HttpClient client = new HttpClient())
            {
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(file, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes.HasValue)
                            {
                                double percentage = (double)totalBytesRead / totalBytes.Value * 100;
                                Console.Write($"\r{percentage:F1}% [" + new string('█', (int)percentage) + new string(' ', 100 - (int)percentage) + "]");
                            }
                        }
                        Console.WriteLine();
                    }
                }
            }
        }
    }
}