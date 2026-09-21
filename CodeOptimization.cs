using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UserOptimizationExample
{
    // Задания 5.1 – 5.3: оптимизация работы с файлами и вычислений
    public class CodeOptimization
    {
        // Размер буфера для файловых операций (по умолчанию у StreamReader/StreamWriter – 1024/4096)
        private const int BufferSize = 8192;

        // 5.3 Кэш вычисленных чисел Фибоначчи: ключ – n, значение – Fib(n)
        private readonly Dictionary<int, long> _fibonacciCache = new Dictionary<int, long>();

        // 5.1 Оптимизированное чтение из файла: асинхронно, с увеличенным буфером
        public async Task ReadFromFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found.");
                return;
            }

            using (var reader = new StreamReader(filePath, Encoding.UTF8, true, BufferSize))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                    Console.WriteLine(line);
            }
        }

        // 5.2 Оптимизированная запись в файл: асинхронно, всей строкой, с увеличенным буфером
        public async Task WriteToFileAsync(string filePath, string data)
        {
            using (var writer = new StreamWriter(filePath, true, Encoding.UTF8, BufferSize))
                await writer.WriteLineAsync(data);
        }

        // 5.3 Числа Фибоначчи с кэшированием (мемоизацией) уже вычисленных значений
        public long Fibonacci(int n)
        {
            if (n <= 1)
                return n;

            if (_fibonacciCache.TryGetValue(n, out long value))
                return value;

            value = Fibonacci(n - 1) + Fibonacci(n - 2);
            _fibonacciCache[n] = value;
            return value;
        }
    }
}
