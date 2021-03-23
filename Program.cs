using System;
using System.Threading.Tasks;
using PuppeteerSharp;
using System.Configuration;
using System.Collections.Generic;

namespace BankOfChinaWebScraper
{
    class Program
    {
        public static bool HEADLESS_BROWSER = ConfigurationManager.AppSettings.Get("headless_browser") == "true" ? true : false;
        public static bool SEARCH_ALL_PAGES = ConfigurationManager.AppSettings.Get("search_all_pages") == "true" ? true : false;
        //if SEARCH_ALL_PAGES == true -> aplication may work very slow due to website misbehaving 
        public static async Task<int> Main()
        {
            //LOCAL VARIABLES
            string url = "https://srh.bankofchina.com/search/whpj/searchen.jsp";
            string date_today = DateTime.Now.ToString("yyyy-MM-dd");
            string date_day_beafore_yesterday = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");
            string output_directory = ConfigurationManager.AppSettings.Get("output_directory_path");
            System.IO.Directory.CreateDirectory(output_directory);
            try
            {
                //INIT BROWSER
                Browser browser = await InitBrowser();

                //VISIT PAGE
                Page page = await browser.NewPageAsync();
                Console.WriteLine("Visiting page: " + url + "...");
                await page.GoToAsync(url);
                Console.WriteLine("Page: " + url + " visited");

                //DATA EXTRACTION  
                //get all currencies    
                Console.WriteLine("Getting all available currencies...");
                var all_currencies = await GetAllCurrencies(page);
                Console.WriteLine("Done");

                //write data for each currency available to matching output file
                foreach (var currency in all_currencies)
                {
                    //set filepath
                    string name_of_the_file = currency + "_" + date_day_beafore_yesterday + "_" + date_today + ".txt";
                    string filepath = output_directory + name_of_the_file;

                    //navigate to currency records
                    Console.WriteLine("Searching records for currency: " + currency + "...");
                    bool data_exist = await SearchDataForCurrency(page, url, date_day_beafore_yesterday, date_today, currency);
                    Console.WriteLine("Searching finished");

                    if (data_exist)
                    {
                        //Load records localy
                        Console.WriteLine("Getting records for currency: " + currency + "...");
                        var header = await GetCurrencyDataHeader(page);
                        var records = await GetCurrencyData(page);
                        Console.WriteLine("Records loaded");

                        //write records to output file
                        Console.WriteLine("Writting records for currency: " + currency + " to output file: " + filepath + "...");
                        await WriteDataToOutputFile(filepath, header, records);
                        Console.WriteLine("Writing finished");
                    }
                    else
                    {
                        //write message 'No records!' to output file
                        using (System.IO.StreamWriter file = new System.IO.StreamWriter(@filepath, false))
                        {
                            await file.WriteLineAsync("No records!");
                            Console.WriteLine("No records for currency: " + currency);
                        }
                    }
                }

                //CLOSE BROWSER
                Console.WriteLine("Closing browser...");
                await browser.CloseAsync();
                Console.WriteLine("Browser closed");

                Console.WriteLine("Program successfully finished");
                return 0;

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return -1;
            }

        }


        public static async Task<Browser> InitBrowser()
        {
            Console.WriteLine("Dowloading browser...");
            await new BrowserFetcher().DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
            Console.WriteLine("Browser downloaded");

            Console.WriteLine("Lunching browser...");
            Browser browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = HEADLESS_BROWSER 
            });
            Console.WriteLine("Browser lunched");

            return browser;
        }

        public static async Task<string[]> GetAllCurrencies(Page page)
        {
            await page.WaitForSelectorAsync("#pjname");
            var elementHandles = await page.XPathAsync($"//select[@id=\"pjname\"]/option[position()>1]");
            //var all_currencies = await page.EvaluateExpressionAsync<string[]>("Array.from(document.querySelectorAll('option')).map(option => option.value);");
            if (elementHandles.Length == 0)
            {
                throw new Exception("No currencies available");
            }
            var all_currencies = await ElementHandlesToArrayOfStrings(elementHandles, "value");
            return all_currencies;
        }

        public static async Task<string[]> ElementHandlesToArrayOfStrings(ElementHandle[] elementHandles, string property)
        {
            string[] return_value = new string[elementHandles.Length];
            int i = 0;
            foreach (var data in elementHandles)
            {
                var jsHandle = await data.GetPropertyAsync(property);
                string data_value = await jsHandle.JsonValueAsync<string>();
                return_value[i++] = data_value;
            }

            return return_value;
        }

        public static async Task<bool> SearchDataForCurrency(Page page, string url, string date1, string date2, string currency)
        {
            //await page.GoToAsync(url);
            await page.WaitForSelectorAsync("input[type='text'][name='erectDate']");
            await page.ClickAsync("input[type='text'][name='erectDate']", new PuppeteerSharp.Input.ClickOptions { ClickCount = 3 });
            await page.TypeAsync("input[type='text'][name='erectDate']", date1);

            await page.WaitForSelectorAsync("input[type='text'][name='nothing']");
            await page.ClickAsync("input[type = 'text'][name = 'nothing']", new PuppeteerSharp.Input.ClickOptions { ClickCount = 3 });
            await page.TypeAsync("input[type='text'][name='nothing']", date2);

            await page.WaitForSelectorAsync("#pjname");
            await page.SelectAsync("#pjname", currency);

            await page.WaitForSelectorAsync("input[type='button'][value='search']");
            await page.ClickAsync("input[type='button'][value='search']");

            bool data_exist = await WaitForSearchingToComplete(page);
            return data_exist;
        }

        public static async Task<bool> WaitForSearchingToComplete(Page page)
        {
            bool data_exist = false;
            bool loaded = false;
            while (!loaded)
            {
                await page.WaitForSelectorAsync(".hui12_20");
                var elementHandles = await page.XPathAsync($"//td[@class=\"hui12_20\"][text()=\"soryy,wrong search word submit,please check your search word!\"]");
                var search_failed_msg = await ElementHandlesToArrayOfStrings(elementHandles, "innerHTML");
                if (search_failed_msg.Length == 0)
                {
                    loaded = true;
                    await page.WaitForSelectorAsync(".hui12_20");
                    var elementHandles2 = await page.XPathAsync($"//td[@class=\"hui12_20\"][text()=\"sorry, no records！\"]");
                    var no_records_msg = await ElementHandlesToArrayOfStrings(elementHandles2, "innerHTML");
                    if (no_records_msg.Length == 0)
                    {
                        data_exist = true;
                    }
                }
                else
                {
                    Console.WriteLine("Website isn't behaving as expected, this may take a while...");
                    await page.WaitForSelectorAsync("input[type='button'][value='search']");
                    await page.ClickAsync("input[type='button'][value='search']");
                }
            }
            return data_exist;
        }

        public static async Task<string[]> GetCurrencyDataHeader(Page page)
        {
            var elementHandles = await page.XPathAsync($"//td[@class=\"lan12_hover\"][text()]");
            var header = await ElementHandlesToArrayOfStrings(elementHandles, "innerHTML");
            return header;
        }

        public static async Task<string[]> GetCurrencyData(Page page)
        {
            if(SEARCH_ALL_PAGES)
            {
                //get page number
                var elementHandles_number_of_pages = await page.XPathAsync($"//span[@class=\"nav_pagenum\"]");
                var pagenum_string_array = await ElementHandlesToArrayOfStrings(elementHandles_number_of_pages, "innerHTML");
                int number_of_pages = 1;
                if (pagenum_string_array.Length != 0)
                {
                    number_of_pages = Int32.Parse(pagenum_string_array[0]);
                }
                else
                {
                    throw new Exception("Could not find number of pages for current currency");
                }

                //get data from every page
                List<string> records = new List<string>();
                for (int curr_page = 1; curr_page <= number_of_pages; ++curr_page)
                {
                    //take data from current page
                    Console.WriteLine("Loading data from page: " + curr_page + "...");
                    var elementHandles_records = await page.XPathAsync($"//td[@class=\"hui12_20\"]");
                    records.AddRange(await ElementHandlesToArrayOfStrings(elementHandles_records, "innerHTML"));
                    Console.WriteLine("Data from page: " + curr_page + " loaded");

                    if (curr_page == number_of_pages) break;
                    //else go to next page
                    bool new_page_loaded_successfully = false;
                    while (!new_page_loaded_successfully)
                    {
                        //go to next page
                        await page.WaitForSelectorAsync("span[class='wcm_pointer nav_go_next']");
                        await page.ClickAsync("span[class='wcm_pointer nav_go_next']");

                        //check if next page loaded successfully
                        await page.WaitForSelectorAsync(".hui12_20");
                        var elementHandles_search_error_msg = await page.XPathAsync($"//td[@class=\"hui12_20\"][text()=\"soryy,wrong search word submit,please check your search word!\"]");
                        var search_error_msg = await ElementHandlesToArrayOfStrings(elementHandles_search_error_msg, "innerHTML");
                        if (search_error_msg.Length != 0)
                        {
                            Console.WriteLine("Website isn't behaving as expected, this may take a while...");
                            //if not, recover to current page
                            bool loaded = false;
                            while (!loaded)
                            {
                                //recovery process:
                                await page.WaitForSelectorAsync(".hui12_20");
                                var elementHandles = await page.XPathAsync($"//td[@class=\"hui12_20\"][text()=\"soryy,wrong search word submit,please check your search word!\"]");
                                var search_failed_msg = await ElementHandlesToArrayOfStrings(elementHandles, "innerHTML");
                                if (search_failed_msg.Length != 0)
                                {
                                    await page.WaitForSelectorAsync("input[type='button'][value='search']");
                                    await page.ClickAsync("input[type='button'][value='search']");
                                }
                                else
                                {
                                    await page.WaitForSelectorAsync(".nav_page");
                                    var elementHandles_nav_pages_links = await page.XPathAsync($"//span[@class=\"nav_page\"]/a");
                                    var nav_pages_links = await ElementHandlesToArrayOfStrings(elementHandles_nav_pages_links, "innerHTML");

                                    bool recovery_start_page_success = true;
                                    int recovery_start_page_number = 1;
                                    foreach(var link in nav_pages_links)
                                    {
                                        int link_pagenum = Int32.Parse(link);
                                        if (link_pagenum <= curr_page && link_pagenum > recovery_start_page_number)
                                        {
                                            recovery_start_page_number = link_pagenum;
                                        }
                                    }
                                    if(recovery_start_page_number != 1)
                                    {
                                        await page.WaitForSelectorAsync("span[class='nav_page']");
                                        await page.ClickAsync("span[class='nav_page'] a[onclick~='(" + recovery_start_page_number + "," + number_of_pages + ")']");

                                        await page.WaitForSelectorAsync(".hui12_20");
                                        var elementHandles_search_error_msg3 = await page.XPathAsync($"//td[@class=\"hui12_20\"][text()=\"soryy,wrong search word submit,please check your search word!\"]");
                                        var search_error_msg3 = await ElementHandlesToArrayOfStrings(elementHandles_search_error_msg3, "innerHTML");
                                        if (search_error_msg3.Length != 0)
                                        {
                                            recovery_start_page_success = false;
                                        }
                                    }
                                    if(recovery_start_page_success)
                                    {
                                        int i;
                                        for (i = recovery_start_page_number; i < curr_page; ++i)
                                        {
                                            await page.WaitForSelectorAsync("span[class='wcm_pointer nav_go_next']");
                                            await page.ClickAsync("span[class='wcm_pointer nav_go_next']");

                                            await page.WaitForSelectorAsync(".hui12_20");
                                            var elementHandles_search_error_msg2 = await page.XPathAsync($"//td[@class=\"hui12_20\"][text()=\"soryy,wrong search word submit,please check your search word!\"]");
                                            var search_error_msg2 = await ElementHandlesToArrayOfStrings(elementHandles_search_error_msg2, "innerHTML");
                                            if (search_error_msg2.Length != 0)
                                            {
                                                break;
                                            }
                                        }
                                        if (i == curr_page)
                                        {
                                            loaded = true;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            //if it is loaded successfully, just continue
                            new_page_loaded_successfully = true;
                        }
                    }
                   
                }

                return records.ToArray();
            }
            else
            {
                //get element from only first page
                var elementHandles_records = await page.XPathAsync($"//td[@class=\"hui12_20\"]");
                var records = await ElementHandlesToArrayOfStrings(elementHandles_records, "innerHTML");
                return records;
            }
            
        }

        public static async Task WriteDataToOutputFile(string filepath, string[] header, string[] records)
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(@filepath, false))
            {
                int number_of_columns = 7;
                //write header
                string line = "";
                for (int i = 0; i < number_of_columns; ++i)
                {
                    line += header[i];
                    if (i != number_of_columns - 1) line += ",";
                }
                file.WriteLine(line);

                //write records
                line = "";
                int number_of_table_fields_writed = 0;
                foreach (var table_field in records)
                {
                    line += table_field;
                    ++number_of_table_fields_writed;
                    if (number_of_table_fields_writed == number_of_columns)
                    {
                        await file.WriteLineAsync(line);
                        line = "";
                        number_of_table_fields_writed = 0;
                    }
                    else
                    {
                        line += ",";
                    }
                }
            }
        }

    }
}
