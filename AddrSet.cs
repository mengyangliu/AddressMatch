﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Text;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace AddressMatch
{

    public class AddrSet
    {

        public static Graph AddrGraph;

        public static string DumpDirectory;

        public static string DiskFilePath;

        private bool _initialized = false;

        private static AddrSet _instance;

        private static object SingleInstanceLock = new object();

        private static ReaderWriterLockSlim rwLock = new ReaderWriterLockSlim();

        #region -----------------------初始化方法-------------------------

        private AddrSet()
        {
            init();

            if (!ResumeFromDisk())
            {
                throw new Exception("从硬盘回复失败");
            }

            _initialized = true;
            
        }

        //参数设置
        private void init()
        {
            DumpDirectory = @"D:\";
            //磁盘文件地址
            DiskFilePath = @"D:\Test.dat";
        }

        private bool ResumeFromDisk()
        {
            if (!File.Exists(DiskFilePath))
            {
                throw new IOException(DiskFilePath + "下面数据文件不存在");
            }
            Stream stream = null;
            try
            {
                stream = new FileStream(DiskFilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                BinaryFormatter formatter = new BinaryFormatter();
                AddrGraph = (Graph)formatter.Deserialize(stream);
                stream.Close();
            }
            catch (System.Exception ex)
            {
                if (stream != null)
                    stream.Close();
                throw new Exception("Deserialization failed! Message: " + ex.Message);
            }
            if (AddrGraph != null && AddrGraph.NodeTable != null && AddrGraph.root != null)
            {
                return true;
            }
            else
            {
                return false;
            }
            
        }

        private void FillHashTable()
        {

        }
        #endregion

        #region -----------------------property-------------------------

        public static AddrSet GetInstance()
        {
            if (_instance == null)
            {
                lock (SingleInstanceLock)
                {
                    if (_instance == null)
                    {
                        _instance = new AddrSet();
                        return _instance;
                    }
                }
            }

            return _instance;

        }

        public bool Initialized
        {
            get { return _initialized; }
        }

        #endregion

        #region -----------------------持久化-------------------------
        //Flush to Disk -------------TODO  Add Header[] to file? e.g. Version, CRC, TimeStamp.....
        public  bool Dump()
        {

            if (DumpDirectory == "" || DumpDirectory == null)
            {
                throw new Exception("未初始化");
            }

            string dumpfile = getFileNameToDump();
            Stream stream = null;
            try
            {
                stream = new FileStream(DumpDirectory + dumpfile, FileMode.Create, FileAccess.Write, FileShare.None);
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, AddrGraph);
                stream.Close();
            }
            catch (System.Exception ex)
            {
                if (stream != null)
                    stream.Close();
                throw new Exception("serialization failed! Message: " + ex.Message);
            }

            Console.WriteLine(" serialize is success!");

            return true;
        }

        private string getFileNameToDump()
        {
            string filename = "AddrSetFile-" + DateTime.Now.ToString("yyyy-MM-dd") + "-" +
                                        DateTime.Now.Hour.ToString();
            int i = 0;
            while (File.Exists(DumpDirectory + filename))
            {
                filename += i.ToString();
                i++;
            }

            filename += ".dat";
            return filename;
        }

        #endregion

        #region -----------------------Retrieval-------------------------
        public  List<GraphNode> RetrievalGraph(Predicate<GraphNode> p)
        {
            List<GraphNode> result = new List<GraphNode>();

            rwLock.EnterReadLock();

            MultiMatchInNext(p, AddrGraph.root, ref result);

            rwLock.ExitReadLock();

            return result;
        }

        private  void MultiMatchInNext(Predicate<GraphNode> p, GraphNode node, ref List<GraphNode> result)
        {
            if (p(node) && !result.Contains(node))
            {
                result.Add(node);
            }
            if (node.NextNodeList == null || node.NextNodeList.Count == 0)
            {
                return;
            }
            foreach (GraphNode nxt_node in node.NextNodeList)
            {
                MultiMatchInNext(p, nxt_node, ref result);
            }
        }

        public  List<GraphNode> ForwardSearchNode(Predicate<GraphNode> p, IList<GraphNode> sourceNodeList)
        {
            List<GraphNode> result = new List<GraphNode>();
            
            rwLock.EnterReadLock();
            foreach (var sourceNode in sourceNodeList)
            {
                MultiMatchInNext(p, sourceNode, ref result);
            }
            
            
            rwLock.ExitReadLock();

            return result;
        }

        public List<GraphNode> ForwardSearchNode(Predicate<GraphNode> p, GraphNode sourceNode)
        {
            List<GraphNode> result = new List<GraphNode>();

            rwLock.EnterReadLock();

            MultiMatchInNext(p, sourceNode, ref result);

            rwLock.ExitReadLock();

            return result;
        }



        #endregion


        #region -----------------------GraphNodeOperation------------------------

        public bool Insert(GraphNode NewNode,GraphNode FatherNode)
        {
            rwLock.EnterWriteLock();

            if (NewNode == null || FatherNode ==null || FatherNode.NextNodeList == null)
            {
                return false;
            }
            if (NewNode.NodeLEVEL <= FatherNode.NodeLEVEL && NewNode.NodeLEVEL != LEVEL.Uncertainty)
            {
                return false;
            }

            TableNode tnode = new TableNode(NewNode);
            Hashtable table = AddrSet.AddrGraph.NodeTable;

            //Add to NodeTable
            if (table.Contains(tnode.Name))
            {
                AppendTableNodeList((TableNode)table[tnode.Name], tnode);
            }
            else
            {
                table.Add(tnode.Name, tnode);
            }

            //Linked to Graph
            FatherNode.NextNodeList.Add(NewNode);

            rwLock.ExitWriteLock();

            return true;
        }
        
        // Need Retrieval the Whole Graph!        --------TODO: Improve?   NEED TESTED
        public bool Delete(GraphNode node)
        {
            rwLock.EnterWriteLock();

            //Delete from NodeTable
            Hashtable table = AddrSet.AddrGraph.NodeTable;
            TableNode tnodelist = table[node.Name] as TableNode;

            //find the relevant node in NodeList
            TableNode tnode = tnodelist;
            while (tnode.Next != null)
            {
                if (tnode.GNode.ID == node.ID)
                {
                    break;
                }
                tnode = tnode.Next;
            }
            if (tnode.GNode.ID != node.ID)
            {
                //Not found in NodeList
                return false;
            }

            if (tnode.Prev == null)          // this node is head
            {
                tnode.Next.Prev = null;
                table.Remove(node.Name);
                table.Add(node.Name,tnode.Next);
            }
            else if(tnode.Next == null)   //this node is tail
            {
                tnode.Prev.Next = null;
            }
            else
            {
                tnode.Prev.Next = tnode.Next;
                tnode.Next.Prev = tnode.Prev;
            }

            //Delete from Graph
            List<GraphNode> gnodelist = RetrievalGraph(delegate(GraphNode p)
            {
                if (p.NextNodeList.Contains(node))
                {
                    return true;
                }
                else
                {
                    return false;
                }
                
            }); 

            foreach (GraphNode resultnode in gnodelist)
            {
                resultnode.NextNodeList.Remove(node);
            }

            rwLock.ExitWriteLock();

            return true;
        }

        
        public bool ReName(GraphNode node, string name)
        {
            rwLock.EnterWriteLock();

            List<GraphNode> gnodelist = FindGNodeListInHashTable(node.Name);
            foreach (GraphNode gnode in gnodelist)
            {
                if (gnode.ID == node.ID)
                {
                    gnode.Name = name;
                }
            }

            rwLock.ExitWriteLock();

            return true;
        }


        private void AppendTableNodeList(TableNode head,TableNode node)
        {
            TableNode current = head;
            while (current.Next != null)
            {
                current = current.Next;
            }
            current.Next = node;
            node.Next = null;
            node.Prev = current;
        }

        #endregion



        #region -----------------------Query In HashTable-------------------------
        public State FindNodeInHashTable(string name)
        {
            rwLock.EnterReadLock();

            State state = new State();
            if (AddrGraph.NodeTable[name] == null)
            {
                state.Name = name;
                state.MaxStateLEVEL = LEVEL.Uncertainty;
                state.MinStateLEVEL = LEVEL.Uncertainty;
                state.NodeCount = 0;
                state.NodeList = null;
                return state;
            }
            TableNode node = AddrGraph.NodeTable[name] as TableNode;
            LEVEL min = node.GNode.NodeLEVEL;
            LEVEL max = node.GNode.NodeLEVEL;
            state.NodeList.Add(node.GNode);
            int i = 1;
            while (node.Next != null)
            {
                min = min < node.Next.GNode.NodeLEVEL ? min : node.Next.GNode.NodeLEVEL;
                max = max > node.Next.GNode.NodeLEVEL ? max : node.Next.GNode.NodeLEVEL;
                state.NodeList.Add(node.Next.GNode);
                node = node.Next;
                i++;
            }
            state.Name = name;
            state.NodeCount = i;

            rwLock.ExitReadLock();

            return state;

        }


        public List<GraphNode> FindGNodeListInHashTable(string name)
        {
            rwLock.EnterReadLock();

            List<GraphNode> resultList = new List<GraphNode>();

            TableNode node = AddrGraph.NodeTable[name] as TableNode;
            resultList.Add(node.GNode);

            while (node.Next != null)
            {
                resultList.Add(node.Next.GNode);
                node = node.Next;
            }

            rwLock.ExitReadLock();

            return resultList;

        }
        #endregion


    }


}
