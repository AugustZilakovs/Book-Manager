﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Data.SQLite;

namespace BookLibraryV1
{
    internal class FileReader
    {
        static Form1 form;
        static Form2 form2;
        static AuthorTableAccessor authorTableAccessor;
        static BookTableAccessor bookTableAccessor;
        GenreTableAccessor genreTableAccessor;
        static ImageTableAccessor imageTableAccessor;
        static Dictionary<String, String> authorList;
        static Dictionary<String, String> bookList;
        static XDocument doc;
        static XNamespace ns = "http://www.gribuser.ru/xml/fictionbook/2.0";
        private static ManualResetEvent mre = new ManualResetEvent(false);
        private delegate void SafeCallDelegate(string text);
        static List<String> failedFiles = new List<string>();
        public static int p = 0;
        static Thread pr;

        public FileReader(Form1 f, AuthorTableAccessor dBAuthor, BookTableAccessor dBBooks, GenreTableAccessor dbGenre, ImageTableAccessor dbImage)
        {
            form = f;
            authorTableAccessor = dBAuthor;
            bookTableAccessor = dBBooks;
            genreTableAccessor = dbGenre;
            imageTableAccessor = dbImage;
        }
        public void populateTables(List<String> files)
        {
            failedFiles = new List<string>();
            Thread counterThreads = new Thread(new ParameterizedThreadStart(read.readBooks));
            //Thread progressBar = new Thread(new ParameterizedThreadStart(progress));
            //form2.Show();
            
            counterThreads.Start(files);
            //progressBar.Start(files.Count);
/*            while (counterThreads.IsAlive)
            {
                form2.progressLbl.Text = $"{p}";
            }*/

        }
        public void editBook(String iD, String directory)
        {
            Dictionary<String, String> bookDetails = bookTableAccessor.getBook(iD);
            List<String> authorDetails = authorTableAccessor.getAuthor(bookTableAccessor.getAuthorId(iD).Trim());
            doc = XDocument.Load($"{bookDetails["Directory"]}");
            IEnumerable<XElement> description = doc.Root.Element(ns + "description").Elements();
            IEnumerable<XElement> titleInfo = description.ElementAt(findTitleInfoIndex(description)).Elements();
            XElement t = description.ElementAt(0);
            int index = findAuthorInfoIndex(titleInfo, description);
            IEnumerable<XElement> authorInfo = titleInfo.ElementAt(index).Elements();

            foreach (XElement author in authorInfo)
            {
                switch (author.Name.ToString())
                {
                    case "{http://www.gribuser.ru/xml/fictionbook/2.0}first-name":
                        author.Value = authorDetails.ElementAt(0);
                        break;
                    case "{http://www.gribuser.ru/xml/fictionbook/2.0}last-name":
                        author.Value = authorDetails.ElementAt(2);
                        break;
                    case "{http://www.gribuser.ru/xml/fictionbook/2.0}middle-name":
                        author.Value = authorDetails.ElementAt(1);
                        break;
                    default:
                        break;
                }
            }
            //garbage code to remove, needs to be changed later
            for (int i = titleInfo.Count() - 1; i >= 0; i--)
            {
                switch (titleInfo.ElementAt(i).Name.ToString())
                {
                    case "{http://www.gribuser.ru/xml/fictionbook/2.0}genre":
                        titleInfo.ElementAt(i).Remove();
                        break;
                    case "{http://www.gribuser.ru/xml/fictionbook/2.0}book-title":
                        titleInfo.ElementAt(i).Value = bookDetails["Title"];
                        break;
                    default: break;
                }
            }
            foreach (String newGenre in bookDetails["Genre"].Trim().Split(' '))
            {
                t.AddFirst(new XElement("Genre", newGenre));
            }

            doc.Save($"{directory}/{bookDetails["Title"]}.fb2");
        }


        public static int findTitleInfoIndex(IEnumerable<XElement> l)
        {
            for (int i = 0; i < l.Count(); i++)
            {
                if (l.ElementAt(i).Name.ToString() == "{http://www.gribuser.ru/xml/fictionbook/2.0}title-info")
                {
                    return i;
                }
            }
            return -1;
        }
        public static int findAuthorInfoIndex(IEnumerable<XElement> l, IEnumerable<XElement> x)
        {
            for (int i = 0; i < l.Count(); i++)
            {
                if (l.ElementAt(i).Name.ToString() == "{http://www.gribuser.ru/xml/fictionbook/2.0}author")
                {
                    return i;
                }
            }
            for (int i = 0; i < x.Count(); i++)
            {
                if (l.ElementAt(i).Name.ToString() == "{http://www.gribuser.ru/xml/fictionbook/2.0}author")
                {
                    return i;
                }
            }
            return 0;
        }
        public static int findPublisherInfoIndex(IEnumerable<XElement> l)
        {
            for (int i = 0; i < l.Count(); i++)
            {
                if (l.ElementAt(i).Name.ToString() == "{http://www.gribuser.ru/xml/fictionbook/2.0}publish-info")
                {
                    return i;
                }
            }
            return 0;
        }


        class read
        {
            static LocalDataStoreSlot localSlot;
            static read()
            {
                localSlot = Thread.AllocateDataSlot();
            }

            public static void readBooks(object l)
            {
                var books = ((IEnumerable)l).Cast<object>().ToList();
                pr = new Thread(new ThreadStart(() =>
                {
                    Form2 form = new Form2(books.Count);
                    form.ShowDialog();
                    while (p < books.Count)
                    {
                        form.progressLbl.Text = $"{p}";
                    }
                }
                ));


                p = 0;
                authorTableAccessor.resetAuthorTable();
                bookTableAccessor.resetBookTable();
                imageTableAccessor.resetCoverTable();

                IEnumerable<XElement> description= Enumerable.Empty<XElement>();
                IEnumerable<XElement> titleInfo = Enumerable.Empty<XElement>();
                IEnumerable<XElement> publisherInfo = Enumerable.Empty<XElement>();
                IEnumerable<XElement> authorInfo = Enumerable.Empty<XElement>();
                int index = 0;
                List<String> tags;
                pr.Start();
                foreach (string file in books)
                {
                    String imageUrl = "";
                    p++;
                    Thread.SetData(localSlot, p);
                    StringBuilder sb = new StringBuilder("");
                    authorList = new Dictionary<String, String>
                {
                    //create new list and new index
                    { "FirstNames", "" },
                    { "LastNames", "" },
                    { "MiddleNames", ""},
                    { "ID", "" }
                };

                    bookList = new Dictionary<String, String>
                {
                    { "Title", "" },
                    { "AuthorId", "" },
                    { "Series", "" },
                    { "SeriesNum", "0" },
                    { "Directory", "" },
                    { "Genre", "" },
                    { "Keywords", "" },
                    { "Annotation", "" },
                    { "Publisher", "" },
                    { "ImageId", "" },
                };
                    try
                    {
                        doc = XDocument.Load($"{file}"); //load file
                        XElement elements = doc.Root.Element(ns + "description");//gets description node
                        IEnumerable<XElement> image = doc.Root.Elements(ns + "binary");

                        try {
                            description = elements.Elements(); //all elements under description
                            titleInfo = description.ElementAt(findTitleInfoIndex(description)).Elements();//opens title info element as it is the first node
                            publisherInfo = description.ElementAt(findPublisherInfoIndex(description)).Elements();
                            index = findAuthorInfoIndex(titleInfo, description);
                            authorInfo = titleInfo.ElementAt(index).Elements();
                        }
                        catch(NullReferenceException err) {
                            failedFiles.Add($"{file}");
                            continue;
                        }
                        //IEnumerable<XElement> image = (from el in doc.Elements("binary") where (string)el.Attribute("id") == "cover.jpg" select el);


                        tags = new List<String>();
                        foreach (XElement authorElements in authorInfo)
                        {
                            switch (authorElements.Name.ToString())
                            {
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}first-name":
                                    authorList["FirstNames"] = (authorElements.Value);
                                    break;
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}last-name":
                                    if (authorElements.ElementsBeforeSelf().Count() < 2)
                                    {
                                        authorList["MiddleNames"] = ("");
                                    }
                                    authorList["LastNames"] = (authorElements.Value);
                                    break;
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}middle-name":
                                    authorList["MiddleNames"] = (authorElements.Value);
                                    break;
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}id":
                                    authorList["ID"] = (authorElements.Value);
                                    break;
                                default:
                                    break;
                            }
                        }

                        foreach (XElement bookElements in titleInfo)
                        {
                            switch (bookElements.Name.ToString())
                            {
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}book-title":
                                    bookList["Title"] = (bookElements.Value);
                                    break;
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}sequence":
                                    if (bookElements.Attribute("name") != null)
                                    {
                                        bookList["Series"] = bookElements.Attribute("name").Value;
                                    }
                                    if (bookElements.Attribute("number") != null)
                                    {
                                        bookList["SeriesNum"] = bookElements.Attribute("number").Value;
                                    }
                                    break;
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}genre":
                                    sb.Append(bookElements.Value + " ");
                                    break;
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}keywords":
                                    bookList["Keywords"] = (bookElements.Value);
                                    break;
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}annotation":
                                    bookList["Annotation"] = (bookElements.Value);
                                    break;
                                default:
                                    break;
                            }
                        }
                        foreach (XElement publisherElement in publisherInfo)
                        {
                            switch (publisherElement.Name.ToString())
                            {
                                case "{http://www.gribuser.ru/xml/fictionbook/2.0}publisher":
                                    bookList["Publisher"] = publisherElement.Value;
                                    break;
                                default: break;
                            }
                        }
                        bookList["Directory"] = file;
                        bookList["Genre"] = sb.ToString();
                        foreach (XElement i in image)
                        {
                            if (i.Attribute("id").Value == "cover.jpg")
                            {
                                imageUrl = i.Value;
                                break;
                            }
                        }
                        authorTableAccessor.addToAuthorTable(authorList);
                        bookList["AuthorId"] = authorTableAccessor.checkAuthorLocation(authorList["ID"]).ToString();
                        imageTableAccessor.addToCoverTable(imageUrl);
                        bookList["ImageId"] = imageTableAccessor.getRecentAdded().ToString();
                        bookTableAccessor.addBook(bookList);
                    }
                    catch (Exception e)
                    {
                        failedFiles.Add($"{file}");
                        continue;
                    }
                }
            }
            private static void progress(object s)
            {
                int size = Convert.ToInt32(s);
                for (int i = 0; i <= size; i++)
                {
                    form.ProgressLbl.Invoke(new Action(() =>
                        form.ProgressLbl.Text = $"Reading Books {i} / {s}"
                    ));
                    Thread.Sleep(100);
                }
            }

            private static void progress()
            {

            }
        }
    }


}
