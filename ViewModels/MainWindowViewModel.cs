using System.Collections.Generic;

#pragma warning disable CS8602 // �����ÿ��ܳ��ֿ����á�
#pragma warning disable CA1822 // ����Ա���Ϊ static

namespace KitX_Dashboard.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public double DB_Width
        {
            get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[1];
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[1] = value;
        }

        public double DB_Height
        {
            get => (double)(Helper.local_db_table.Query(1).ReturnResult as List<object>)[2];
            set => (Helper.local_db_table.Query(1).ReturnResult as List<object>)[2] = value;
        }
    }
}

#pragma warning restore CA1822 // ����Ա���Ϊ static
#pragma warning restore CS8602 // �����ÿ��ܳ��ֿ����á�
