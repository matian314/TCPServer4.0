using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Reflection;
namespace TCPServer4._0
{
    class Program
    {
        private static string Logger { get { return DateTime.Now.ToString("yyyyMMdd"); } }
        static void Main(string[] args)
        {
            //降低该进程的优先级，为较低
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;

            try
            {
                while (true)
                {
                    RunTcpServer();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"error code: {ex.Message}");
                Console.WriteLine($"{DateTime.Now}");
                Console.WriteLine($"故障原因: {ex.Message}");
                //延迟10s后关闭程序
                Thread.Sleep(TimeSpan.FromSeconds(10));
                Process.Start(Assembly.GetExecutingAssembly().Location);
                Process.GetCurrentProcess().Kill();
                return;
            }
        }
        private static DefaultTraceListener listener = null; 
        public static void RunTcpServer()
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
                    hostIP = IPAddress.Parse(Settings.Default.HostIP);
                }
                if (hostEP == null)
                {
                    hostEP = new IPEndPoint(hostIP, Convert.ToInt32(Settings.Default.HostPort));
                }
                if( socket == null)
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
                    if( listener == null )
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
                    if( oldLogTime != Logger)
                    {
                        //设置日志文件名,logDate变量保存上一次日志的日期
                        listener.LogFileName = $"TCPServer{Logger}.log";
                        oldLogTime = Logger;
                        string deleteLogName = $@"{Directory.GetCurrentDirectory()}\TCPServer{DateTime.Now.AddDays(0).ToString("yyyyMMdd")}.log";
                        File.Delete(deleteLogName);
                    }
                    Ping pingSender = new Ping();
                    PingReply pingReply = pingSender.Send(Settings.Default.ClientIP, 1000);
                    if (pingReply.Status != IPStatus.Success)
                    {
                        Console.WriteLine(DateTime.Now);
                        Console.WriteLine($"{Settings.Default.ClientIP}网络连接异常，类型为{pingReply.Status.ToString()}");
                        Trace.WriteLine(DateTime.Now);
                        Trace.WriteLine($"{Settings.Default.ClientIP}网络连接异常，类型为{pingReply.Status.ToString()}");
                    }

                    //等待连接
                    //Trace.WriteLine($"\n\r{DateTime.Now}");
                    //Trace.WriteLine($"waiting for the client");
                    //接受客户端连接的请求，并配置超时时间
                    client = socket.Accept();
                    client.SendTimeout = Settings.Default.TimeOut;
                    client.ReceiveTimeout = Settings.Default.TimeOut;
                    //检查客户端的IP地址是否符合要求,如果不符合要求，关闭该Socket
                    if (((IPEndPoint)client.RemoteEndPoint).Address.ToString() != Settings.Default.ClientIP)
                    {
                        Trace.WriteLine($"\n\r{DateTime.Now}");
                        Trace.WriteLine($"Remote ip: {((IPEndPoint)client.RemoteEndPoint).Address.ToString()},going to shut down");
                        //Console.WriteLine($"\n\r{DateTime.Now}");
                        //Console.WriteLine($"Remote ip: {((IPEndPoint)client.RemoteEndPoint).Address.ToString()},going to shut down");
                        client.Shutdown(SocketShutdown.Both);
                        client.Disconnect(true);
                        if (client.Connected)
                        {
                            Trace.WriteLine("client still connecting after disconnected");
                        }
                        continue;
                    }
                    //Trace.WriteLine($"{DateTime.Now}");
                    //Trace.WriteLine($"connect client ip: {((IPEndPoint)client.RemoteEndPoint).Address}\tport:{((IPEndPoint)client.RemoteEndPoint).Port}");
                    //Console.WriteLine($"{DateTime.Now}");
                    //Console.WriteLine($"连接客户端IP地址为: {((IPEndPoint)client.RemoteEndPoint).Address}\t端口号:{((IPEndPoint)client.RemoteEndPoint).Port}");
                    //接收车号。如果没有接到回应，则重发；最多重发5次
                    for (int i = 0; i < 5; i++)
                    {
                        if (sendTrainID(client))
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

                    //接收发送过来的文件
                    //如果接收不成功，重新发送
                    for (int i = 0; i < 5; i++)
                    {
                        if (ReceiveFile(client))
                        {
                            break;
                        }
                        Trace.WriteLine($"\n\r{DateTime.Now}");
                        Trace.WriteLine($"receive file failure for {i + 1} times");
                        //Console.WriteLine($"\n\r{DateTime.Now}");
                        //Console.WriteLine($"receive file failure for {i + 1} times");
                        //如果第五次都没有接收成功，直接退出
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
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"{ex.Message}");
                Console.WriteLine($"{DateTime.Now}");
                Console.WriteLine($"服务遇到异常，正在重启...");
                if (client != null )
                {//shutdown本身可能会导致异常退出，如网络不通时会导致程序关闭
                    client.Close();
                }
                Thread.Sleep(10000);
                return;
            }
            finally
            {//如果因为异常退出，则关闭socket重启
                if(socket != null)
                {
                    //socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
            }
        }
        public static bool SendFile(Socket client)
        {
            //变量声明
            //接收信息的配置
            try
            {
                //检索filePath文件夹是否有文件存在
                DirectoryInfo dr_FS = new DirectoryInfo(Settings.Default.sendFSFilePath);
                DirectoryInfo dr_Sound = new DirectoryInfo(Settings.Default.sendSoundFilePath);
                FileInfo[] files_FS = dr_FS.GetFiles();
                FileInfo[] files_Sound = dr_Sound.GetFiles();

                //如果该文件夹内有内容，依次把每个文件发送出去
                //先发送文件数量
                byte[] fileNum = new byte[5];
                fileNum = Encoding.UTF8.GetBytes((files_FS.Length + files_Sound.Length).ToString("D5"));
                client.Send(fileNum);

                foreach (FileInfo file in files_FS)
                {
                    //打开文件
                    FileStream fs = new FileStream(file.FullName, FileMode.Open);
                    //读取文件内容
                    byte[] fileContent = new byte[(int)file.Length];
                    fs.Read(fileContent, 0, (int)file.Length);
                    fs.Close();
                    //文件名的长度
                    byte[] nameLength = Encoding.UTF8.GetBytes(file.Name.Length.ToString("D2"));
                    //文件名
                    byte[] name = Encoding.UTF8.GetBytes(file.Name);
                    //文件长度
                    byte[] length = Encoding.UTF8.GetBytes(file.Length.ToString("D10"));
                    //发送内容
                    byte[] send = new byte[nameLength.Length + name.Length + length.Length + fileContent.Length];
                    //将四部分内容都复制到send中，一起发送
                    //public static void Copy(Array sourceArray,int sourceIndex,Array destinationArray,int destinationIndex,int length)
                    Array.Copy(nameLength, 0, send, 0, nameLength.Length);
                    Array.Copy(name, 0, send, nameLength.Length, name.Length);
                    Array.Copy(length, 0, send, nameLength.Length + name.Length, length.Length);
                    Array.Copy(fileContent, 0, send, nameLength.Length + name.Length + length.Length, fileContent.Length);
                    //发送信息，并在成功后删除源文件
                    client.Send(send);
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    Trace.WriteLine($"send file name{file.FullName},file length:{ file.Length / 1000 }KB");
                    //Console.WriteLine($"\n\r{DateTime.Now}");
                    //Console.WriteLine($"send file name{file.FullName},file length:{ file.Length / 1000 }KB");
                }
                foreach (FileInfo file in files_Sound)
                {
                    //打开文件
                    FileStream fs = new FileStream(file.FullName, FileMode.Open);
                    //读取文件内容
                    byte[] fileContent = new byte[(int)file.Length];
                    fs.Read(fileContent, 0, (int)file.Length);
                    fs.Close();
                    //文件名的长度
                    byte[] nameLength = Encoding.UTF8.GetBytes(file.Name.Length.ToString("D2"));
                    //文件名
                    byte[] name = Encoding.UTF8.GetBytes(file.Name);
                    //文件长度
                    byte[] length = Encoding.UTF8.GetBytes(file.Length.ToString("D10"));
                    //发送内容
                    byte[] send = new byte[nameLength.Length + name.Length + length.Length + fileContent.Length];
                    //将四部分内容都复制到send中，一起发送
                    //public static void Copy(Array sourceArray,int sourceIndex,Array destinationArray,int destinationIndex,int length)
                    Array.Copy(nameLength, 0, send, 0, nameLength.Length);
                    Array.Copy(name, 0, send, nameLength.Length, name.Length);
                    Array.Copy(length, 0, send, nameLength.Length + name.Length, length.Length);
                    Array.Copy(fileContent, 0, send, nameLength.Length + name.Length + length.Length, fileContent.Length);
                    //发送信息，并在成功后删除源文件
                    client.Send(send);
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    Trace.WriteLine($"send file name{file.FullName},file length:{ file.Length / 1000 }KB");
                    //Console.WriteLine($"\n\r{DateTime.Now}");
                    //Console.WriteLine($"send file name{file.FullName},file length:{ file.Length / 1000 }KB");
                }
                if (ReceiveConfirm(client))
                {
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    //如果收到确认，删除发送的文件，返回成功
                    foreach (FileInfo file in files_Sound)
                    {
                        file.Delete();
                        Trace.WriteLine($"delete file: {file.Name}");
                    }
                    foreach (FileInfo file in files_FS)
                    {
                        file.Delete();
                        Trace.WriteLine($"delete file: {file.Name}");
                    }
                    return true;
                }
                else
                {
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    Trace.WriteLine("server receive file failed\n\rgoing to send again");
                    return false;
                }
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive trainID socket error");
                Trace.WriteLine($"error code :{ex.Message}");
                //Console.WriteLine($"\n\r{DateTime.Now}");
                //Console.WriteLine($"receive trainID socket error");
                //Console.WriteLine($"error code :{ex.Message}");
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive trainID: {ex.Message}");
                //Console.WriteLine($"\n\r{DateTime.Now}");
                //Console.WriteLine($"receive trainID: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive trainID failed.\n\rerror code: {ex.Message}");
                //Console.WriteLine($"\n\r{DateTime.Now}");
                //Console.WriteLine($"receive trainID failed.\n\rerror code: {ex.Message}");
                return false;
            }
        }

        private static string oldTrainID;
        private static bool ReceiveTrainID(Socket socket)
        {
            FileStream fs = null;
            StreamWriter ws = null;
            try
            {
                //接收车号信息，固定车号为11位
                byte[] revBuffer = new byte[11];
                socket.Receive(revBuffer, 11, SocketFlags.None);
                string TrainID = Encoding.UTF8.GetString(revBuffer);
                if (oldTrainID == TrainID)
                {
                    SendConfirm(socket, true);
                    return true;
                }
                oldTrainID = TrainID;

                //获取接收车号的文件路径,将新车号写入
                string filePath = Settings.Default.serverTrainIDPath;
                fs = new FileStream(filePath, FileMode.Create);
                ws = new StreamWriter(fs);
                ws.Write(TrainID);
                ws.Flush();

                Console.WriteLine($"\n\r{DateTime.Now}");
                Console.WriteLine($"update trainID:{TrainID}");
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"update trainID:{TrainID}");
                SendConfirm(socket, true);
                return true;
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive trainID socket error");
                Trace.WriteLine($"error code :{ex.Message}");
                //Console.WriteLine($"\n\r{DateTime.Now}");
                //Console.WriteLine($"receive trainID socket error");
                //Console.WriteLine($"error code :{ex.Message}");
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive trainID: {ex.Message}");
                //Console.WriteLine($"\n\r{DateTime.Now}");
                //Console.WriteLine($"receive trainID: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive trainID failed.\n\rerror code: {ex.Message}");
                //Console.WriteLine($"\n\r{DateTime.Now}");
                //Console.WriteLine($"receive trainID failed.\n\rerror code: {ex.Message}");
                return false;
            }
            finally
            {
                //关闭流
                if (ws != null)
                {
                    ws.Close();
                }
                if (fs != null)
                {
                    fs.Close();
                }

            }
        }

        private static bool sendTrainID(Socket socket)
        {
            //读取车号信息，车号为11位
            byte[] sendBuffer = new byte[11];
            //通过读写车号文件，获取trainID
            string filePath = Settings.Default.serverTrainIDPath;
            FileStream fs = null;
            StreamReader ws = null;
            string trainID;
            try
            {
                fs = new FileStream(filePath, FileMode.Open);
                ws = new StreamReader(fs);
                trainID = ws.ReadLine();

                //发送trainID值
                sendBuffer = Encoding.UTF8.GetBytes(trainID);
                socket.Send(sendBuffer);
                if( oldTrainID != trainID )
                {
                    oldTrainID = trainID;
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    Trace.WriteLine($"send trainID: {trainID}");
                    Console.WriteLine($"\n\r{DateTime.Now}");
                    Console.WriteLine($"send trainID: {trainID}");
                }


                return ReceiveConfirm(socket);
            }
            catch (FileNotFoundException)
            {
                Trace.WriteLine($"send trainID failure:\n\rfile: {filePath} not founde");
                //Console.WriteLine($"send trainID failure:\n\rfile: {filePath} not founde");
                return false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"send trainID failure:\n\rerror code: {ex.Message}");
                //Console.WriteLine($"send trainID failure:\n\rerror code: {ex.Message}");
                return false;
            }
            finally
            {//关闭流
                if (ws != null)
                {
                    ws.Close();
                }
                if (fs != null)
                {
                    fs.Close();
                }
            }
        }
        private static bool ReceiveConfirm(Socket socket)
        {
            try
            {
                byte[] confirm = new byte[1];
                socket.Receive(confirm, 1, SocketFlags.None);
                return (Encoding.UTF8.GetString(confirm) == "1");
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive confirm socket error");
                Trace.WriteLine($"error code :{ex.Message}");
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive confirm: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"receive check failure. error code: {ex.Message}");
                return false;
            }
        }

        private static void SendConfirm(Socket socket, bool isSuccess)
        {//确认信息，1是成功，0是失败
            try
            {
                byte[] sendBuffer = new byte[1];
                if (isSuccess)
                {
                    sendBuffer = Encoding.UTF8.GetBytes("1");
                }
                else
                {
                    sendBuffer = Encoding.UTF8.GetBytes("0");
                }
                socket.Send(sendBuffer);
            }
            catch (SocketException ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"send confirm socket error");
                Trace.WriteLine($"error code :{ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"\n\r{DateTime.Now}");
                Trace.WriteLine($"send check failure. error code: {ex.Message}");
                return;
            }

        }

        public static bool ReceiveFile(Socket client)
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
                int fsNum = 0, axlNum = 0, errorNum = 0, datNum = 0, wavNum = 0;
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
                        switch (flag)
                        {
                            case "1":
                                //接收.FS文件
                                filePath = Settings.Default.receiveFSFilePath;
                                break;
                            case "2":
                                //接收axl文件和wav文件
                                filePath = Settings.Default.receiveSoundFilePath + DateTime.Now.ToString("yyyyMMdd") + @"\SAxleFiles\";
                                Directory.CreateDirectory(filePath);
                                break;
                            case "3":
                                //接收故障轴承声音文件
                                filePath = Settings.Default.receiveErrorFilePath;
                                break;
                            case "4":
                                //接收.dat文件
                                filePath = Settings.Default.receiveDatFilePath + DateTime.Now.ToString("yyyyMMdd") + @"\" + oldTrainID + @"\FS\";
                                Directory.CreateDirectory(filePath);
                                break;
                            case "5":
                                filePath = Settings.Default.receiveWavFilePath + DateTime.Now.ToString("yyyyMMdd") + @"\wav\";
                                Directory.CreateDirectory(filePath);
                                break; 
                        }
                        #region "根据文件后缀，将接收到的文件放入不同的文件夹中"
                        /*
                        string suffix = fileName.Substring(fileName.LastIndexOf(".") + 1).ToUpper();
                        switch (suffix)
                        {
                            case "FS":
                            case "NS":
                                filePath = Settings.Default.receiveFSFilePath;
                                break;
                            case "AXL":
                                filePath = Settings.Default.receiveSoundFilePath;
                                if (filePath.Last<char>() != '\\')
                                {
                                    filePath += '\\';
                                }
                                filePath += DateTime.Now.ToString("yyyyMMdd") + @"\SAxleFiles";
                                Directory.CreateDirectory(filePath);
                                break;
                            case "WAV":
                                filePath = Settings.Default.receiveSoundFilePath;
                                if (filePath.Last<char>() != '\\')
                                {
                                    filePath += '\\';
                                }
                                filePath += DateTime.Now.ToString("yyyyMMdd") + @"\SWavFiles";
                                Directory.CreateDirectory(filePath);
                                break;
                            default:
                                filePath = @"~\";
                                break;
                        }
                        if (filePath.Last<char>() != '\\')
                        {
                            filePath += '\\';
                        }
                        */
                        #endregion

                        //创建文件流，然后让文件流来根据路径创建一个文件
                        //Create模式是创建或覆盖
                        fs = new FileStream(filePath + fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

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
                        switch( flag )
                        {
                            case "1":
                                fsNum++;
                                break;
                            case "2":
                                axlNum++;
                                break;
                            case "3":
                                errorNum++;
                                break;
                            case "4":
                                datNum++;
                                break;
                            case "5":
                                wavNum++;
                                File.Copy(filePath + fileName, Settings.Default.receiveWavFilePath2 + fileName, true);
                                break;
                            default:
                                break;
                        }
                    }
                    finally
                    {
                        if( fs != null)
                        {
                            fs.Close();
                        }
                    }
                }
                SendConfirm(client, true);
                //接收文件总结
                if (fileNumber != 0)
                {
                    Console.WriteLine($"\n\r{DateTime.Now}");
                    Console.WriteLine($"接收{fsNum}个FS文件，{axlNum}个AXL，{errorNum}个故障轴承文件，{datNum}个DAT文件,{wavNum}个WAV文件");
                    Trace.WriteLine($"\n\r{DateTime.Now}");
                    Trace.WriteLine($"receive fs:{fsNum}  ,axl:{axlNum}  ,error:{errorNum}  ,dat:{datNum}，wav: {wavNum}");
                }

                return true;
            }
            catch (SocketException e)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"receive file socket exception");
                Trace.WriteLine($"error message: {e.Message}");
                throw;
            }
            catch (ObjectDisposedException e)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"receive file socket exception");
                Trace.WriteLine($"error message: {e.Message}");

                throw;
            }
            catch (Exception e)
            {
                Trace.WriteLine($"{DateTime.Now}");
                Trace.WriteLine($"receive file failed");
                Trace.WriteLine($"error message: {e.Message}");
                SendConfirm(client, false);
                return false;
            }

        }
    }
}
