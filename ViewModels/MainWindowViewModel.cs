using System;
using System.Collections.Generic;
using System.Text;
using BasicHelper.LiteDB;

#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning disable CA1822 // ����Ա���Ϊ static

namespace KitX_Dashboard.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public double DB_Width => (double)(((Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("Windows").ReturnResult as DataTable)
            .Query(1).ReturnResult as List<object>)[1];

        public double DB_Height => (double)(((Program.LocalDataBase
            .GetDataBase("Dashboard_Settings").ReturnResult as DataBase)
            .GetTable("Windows").ReturnResult as DataTable)
            .Query(1).ReturnResult as List<object>)[2];

    }
}

#pragma warning restore CA1822 // ����Ա���Ϊ static
#pragma warning restore CS8602 // �����ÿ��ܳ��ֿ����á�
