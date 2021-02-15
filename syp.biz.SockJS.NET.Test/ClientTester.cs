﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using syp.biz.SockJS.NET.Client;
using syp.biz.SockJS.NET.Common.Interfaces;

namespace syp.biz.SockJS.NET.Test
{
    [SuppressMessage("ReSharper", "UnusedType.Global")]
    internal class ClientTester : ITestModule
    {
        public async Task Execute()
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                var config = Configuration.Factory.BuildDefault("http://localhost:9999/echo");
                config.Logger = new ConsoleLogger();
                config.DefaultHeaders = new WebHeaderCollection
                {
                    {HttpRequestHeader.UserAgent, "Custom User Agent"},
                    {"application-key", "foo-bar"}
                };

                var sockJs = (IClient)new Client.SockJS(config);

                sockJs.Connected += async (sender, e) =>
                {
                    try
                    {
                        Console.WriteLine("****************** Main: Open");
                        await sockJs.Send(JsonConvert.SerializeObject(new { foo = "bar" }));
                        await sockJs.Send("test");
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                };
                sockJs.Message += async (sender, msg) =>
                {
                    try
                    {
                        Console.WriteLine($"****************** Main: Message: {msg}");
                        if (msg != "test") return;
                        Console.WriteLine("****************** Main: Got back echo -> sending shutdown");
                        await sockJs.Disconnect();
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                };
                sockJs.Disconnected += (sender, e) =>
                {
                    try
                    {
                        Console.WriteLine("****************** Main: Closed");
                        tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                };

                await sockJs.Connect();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                throw;
            }

            await tcs.Task;
        }
    }
}
