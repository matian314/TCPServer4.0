using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace TCPServerAEI
{
    class Program
    {
        static void Main(string[] args)
        {
            //降低该进程的优先级，为较低
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
            try
            {
                while (true)
                {
                    RunServer();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"error code: {ex.Message}");
                Console.WriteLine($"{DateTime.Now}");
                Console.WriteLine($"故障原因: {ex.Message}");
                //延迟5s后关闭程序
                Thread.Sleep(5000);
                return;
            }
        }
        private static DefaultTraceListener listener = null;
        private static string logDate { get { return DateTime.Now.ToString("yyyyMMdd"); } }
        public static void  RunServer()
        {
            Socket client = null;
            Socket socket = null;
            IPAddress hostIP = null;
            IPEndPoint hostEP = null;
            string oldLogTime = null;
            try
            {

                //配置服务器，开始接收客户端请求
                if (hostIP == null)
                {
                    hostIP = IPAddress.Parse(Settings2.Default.HostIP);
                }
                if (hostEP == null)
                {
                    hostEP = new IPEndPoint(hostIP, Convert.ToInt32(Settings2.Default.HostPort));
                }
                if (socket == null)
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                }
                if (!socket.IsBound)
                {
                    socket.Bind(hostEP);
                    //指定socket队列中最多可容纳的等待接受的传入连接数为3个，最多有3个客户端向服务器发送请求
                    socket.Listen(3);
                }
                while (true)
                {
                    if (listener == null)
                    {
                        //开启、配置日志文件
                        listener = Trace.Listeners[0] as DefaultTraceListener;
                        if (listener == null)
                        {
                            //如果Listener中没有DefaultTraceListener,则创建一个
                            listener = new DefaultTraceListener();
                            Trace.Listeners.Add(listener);
                        }
                    }
                    if (oldLogTime != logDate)
                    {
                        //设置日志文件名,logDate变量保存上一次日志的日期
                        listener.LogFileName = $"TCPServerAEI{logDate}.log";
                        oldLogTime = logDate;
                        string deleteLogName = $@"{Directory.GetCurrentDirectory()}\TCPServer{DateTime.Now.AddDays(0).ToString("yyyyMMdd")}.log";
                        File.Delete(deleteLogName);
                    }

                    //等待连接
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    Trace.WriteLine($"waiting for the client");
                    //接受客户端连接的请求,并将超时时间设置为5s
                    client = socket.Accept();
                    client.SendTimeout = Settings2.Default.TimeOut;
                    client.ReceiveTimeout = Settings2.Default.TimeOut;
                    Trace.WriteLine($"{DateTime.Now}");
                    Trace.WriteLine($"connect client ip: {((IPEndPoint)client.RemoteEndPoint).Address}\tport:{((IPEndPoint)client.RemoteEndPoint).Port}");
                    //Console.WriteLine($"{DateTime.Now}");
                    //Console.WriteLine($"连接客户端IP地址为: {((IPEndPoint)client.RemoteEndPoint).Address}\t端口号:{((IPEndPoint)client.RemoteEndPoint).Port}");
                    //接收车号。如果没有接到回应，则重发；最多重发5次
                    for (int i = 0; i < 5; i++)
                    {
                        if (ReceiveAEI(client))
                        {
                            break;
                        }
                        Trace.WriteLine($"\n\r{DateTime.Now}");
                        Trace.WriteLine($"send train number failure for {i + 1} times");
                        //Console.WriteLine($"\n\r{DateTime.Now}");
                        //Console.WriteLine($"send train number failure for {i + 1} times");
                        if (i == 4)
                        {
                            if (client.Connected)
                            {
                                client.Shutdown(SocketShutdown.Both);
                                client.Disconnect(true);
                            }
                            return;
                        }
                    }
                    //关闭client
                    if (client != null)
                    {
                        client.Shutdown(SocketShutdown.Both);
                        client.Disconnect(true);
                    }
                    else
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(DateTime.Now);
                Trace.WriteLine($"{ex.Message}");
                //Console.WriteLine($"{DateTime.Now}");
                //Console.WriteLine($"{ex.Message}");
                Console.WriteLine(DateTime.Now);
                Console.WriteLine($"服务遇到异常，正在重启...");
                if (client != null)
                {
                    //client.Shutdown(SocketShutdown.Both);
                    client.Close();
                }
                Thread.Sleep(10000);
                return;
            }
            finally
            {//如果因为异常退出，关闭socket重启
                if( socket != null)
                {
                    //socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
            }
        }
        public static bool ReceiveAEI( Socket client )
        {
            //字节数组，一次接收数据的长度为 65536 字节  
            byte[] receiveBuffer = new byte[65536];

            //接收信息的配置
            string filePath = null;

            try
            {

                //接收头部文件，读取字节数是根据配置文件中的Settings.Default.receivefileNameLength属性
                //函数原型public int Receive(byte[] buffer,int size,SocketFlags socketFlags)
                //返回值为接收到的字节数
                //首先确定接收要接收多少个文件
                client.Receive(receiveBuffer, 5, SocketFlags.None);
                int fileNumber = Int32.Parse(Encoding.UTF8.GetString(receiveBuffer, 0, 5));
                for (int i = 0; i < fileNumber; i++)
                {
                    FileStream fs = null;
                    try
                    {
                        //变量声明
                        //返回本次接收内容的字节数  
                        int bytes = 0;
                        //文件的长度，不包含报文的头部体
                        int fileLength = 0;
                        //已经接受的字节数
                        int receivedLength = 0;
                        //先发送的1个字节是标志位，决定了如何处理文件
                        client.Receive(receiveBuffer, 1, SocketFlags.None);
                        string flag = Encoding.UTF8.GetString(receiveBuffer, 0, 1);
                        //接下来发送的2个字节代表文件名的长度（不足10用0补齐）
                        bytes = client.Receive(receiveBuffer, 2, SocketFlags.None);
                        int fileNameLength = Int32.Parse(Encoding.UTF8.GetString(receiveBuffer, 0, 2));
                        //根据前两个字节代表文件名长度接收文件名
                        bytes = client.Receive(receiveBuffer, fileNameLength, SocketFlags.None);
                        string fileName = Encoding.UTF8.GetString(receiveBuffer, 0, fileNameLength);
                        //存放路径由配置文件中的receivePath决定
                        filePath = Settings2.Default.receivePath;
                        if (!Directory.Exists(filePath))
                        {
                            Console.WriteLine($"文件目录{filePath}不存在，正在创建");
                            Trace.WriteLine($"文件目录{filePath}不存在，正在创建");
                            Directory.CreateDirectory(filePath);
                        }

                        //创建文件流，然后让文件流来根据路径创建一个文件
                        //Create模式是创建或覆盖
                        fs = new FileStream(filePath +@"\" + fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

                        //接收文件长度，格式为D10,单个文件最长为2^10字节（2GB）

                        bytes = client.Receive(receiveBuffer, 10, 0);
                        //将读取的字节数转换为长度
                        fileLength = Convert.ToInt32(Encoding.UTF8.GetString(receiveBuffer, 0, bytes));

                        //接收正式的文件内容
                        while (receivedLength < fileLength)
                        {//如果要接收的内容一次接收不完，就接收65536字节
                         //否则就接收全部剩余的字节
                            if (receivedLength + receiveBuffer.Length <= fileLength)
                            {
                                bytes = client.Receive(receiveBuffer, receiveBuffer.Length, 0);
                                receivedLength += bytes;
                                fs.Write(receiveBuffer, 0, bytes);
                            }
                            else
                            {
                                bytes = client.Receive(receiveBuffer, fileLength - receivedLength, 0);
                                receivedLength += bytes;
                                fs.Write(receiveBuffer, 0, bytes);
                            }
                        }
                        //保存接收的内容
                        fs.Flush();
                        Trace.WriteLine($"\n\r{DateTime.Now}");
                        Trace.WriteLine($"receiving file: {fileName}");
                        Trace.WriteLine($"file length: { fileLength / 1000.0}KB");
                        //Console.WriteLine($"\n\r{DateTime.Now}");
                        //Console.WriteLine($"receiving file: {fileName}");
                        //Console.WriteLine($"file length: { fileLength / 1000.0}KB");
                    }
                    finally
                    {
                        if (fs != null)
                        {
                            fs.Close();
                        }
                    }
                }
                //接收文件总结
                if (fileNumber != 0)
                {
                    Console.WriteLine($"\n\r{DateTime.Now}");
                    Console.WriteLine($"接收{fileNumber}个AEI文件");
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    Trace.WriteLine($"接收{fileNumber}个AEI文件");
                }

                return true;
            }
            catch (SocketException e)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"receive file socket exception");
                Trace.WriteLine($"error message: {e.Message}");
                //Console.WriteLine($"{DateTime.Now}");
                //Console.WriteLine($"receive file socket exception");
                //Console.WriteLine($"error message: {e.Message}");
                throw;
            }
            catch (ObjectDisposedException e)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"receive file socket exception");
                Trace.WriteLine($"error message: {e.Message}");
                //Console.WriteLine($"{DateTime.Now}");
                //Console.WriteLine($"receive file socket exception");
                //Console.WriteLine($"error message: {e.Message}");
                throw;
            }
            catch (Exception e)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"receive file failed");
                Trace.WriteLine($"error message: {e.Message}");
                //Console.WriteLine($"{DateTime.Now}");
                //Console.WriteLine($"receive file failed");
                //Console.WriteLine($"error message: {e.Message}");
                return false;
            }

        }
    }


}
