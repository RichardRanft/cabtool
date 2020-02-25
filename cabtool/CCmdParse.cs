using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace cabtool
{
    public class CCmdParser
    {
        private StringDictionary m_argDictionary;
        private Dictionary<String, KeyValuePair<EArgType, bool>> m_argRequired;
        private String m_state;

        public StringDictionary Options
        {
            get { return m_argDictionary; }
        }

        public CCmdParser()
        {
            m_state = "";
            m_argDictionary = new StringDictionary();
            m_argRequired = new Dictionary<String, KeyValuePair<EArgType, bool>>();
        }

        public CCmdParser(Dictionary<String, KeyValuePair<EArgType, bool>> argList)
        {
            m_state = "";
            m_argDictionary = new StringDictionary();
            m_argRequired = argList;
            foreach (String argKey in argList.Keys)
            {
                m_argDictionary.Add(argKey, "");
            }
        }

        public bool Parse(String[] args)
        {
            foreach (String arg in args)
            {
                if (m_argDictionary.ContainsKey(arg))
                {
                    m_state = arg;
                    if (m_argRequired[m_state].Key == EArgType.FLAG)
                    {
                        m_argDictionary[m_state] = "true";
                        m_state = "";
                    }
                }
                else
                {
                    m_argDictionary[m_state] = arg;
                    m_state = "";
                }
            }
            foreach (String filled in m_argDictionary.Keys)
            {
                if (m_argRequired[filled].Value && m_argDictionary[filled].Equals(""))
                    return false;
            }
            return true;
        }
    }

    public enum EArgType
    {
        FLAG,
        VALUE
    }
}
