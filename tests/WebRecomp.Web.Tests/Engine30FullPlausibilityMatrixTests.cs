using System.Text;
using System.Text.Json;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class Engine30FullPlausibilityMatrixTests
{
    private const string FixtureBase64 = "W3siV29ya291dFR5cGVDb2RlIjoiUkVTVCIsIkRpc3BsYXlOYW1lIjoiUmVzdCIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJSRVNUX1JFQ09WRVJZX09USEVSIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6Ik1PQklMSVRZX1JFQ09WRVJZIiwiRGlzcGxheU5hbWUiOiJNb2JpbGl0eSAvIFJlY292ZXJ5IiwiV29ya291dENhdGVnb3J5Q29kZSI6IlJFU1RfUkVDT1ZFUllfT1RIRVIiLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiR0VORVJBTF9DQVJESU8iLCJEaXNwbGF5TmFtZSI6IkdlbmVyYWwgQ2FyZGlvIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiRUFTWV9DQVJESU8iLCJEaXNwbGF5TmFtZSI6IkVhc3kgQ2FyZGlvIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiWk9ORTJfQ0FSRElPIiwiRGlzcGxheU5hbWUiOiJab25lIDIgQ2FyZGlvIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiV0FMS0lORyIsIkRpc3BsYXlOYW1lIjoiV2Fsa2luZyIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJDT05ESVRJT05JTkdfQ0FSRElPIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6IlJVTk5JTkciLCJEaXNwbGF5TmFtZSI6IlJ1bm5pbmciLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiQ09ORElUSU9OSU5HX0NBUkRJTyIsIkRlZmF1bHRNaW51dGVzIjowLjAwLCJEZWZhdWx0UmVzdFNlYyI6MCwiUHJvZmlsZVZlcnNpb25Db2RlIjoiIiwiUHJvZmlsZVZlcnNpb25OdW1iZXIiOm51bGwsIlByb2ZpbGVKc29uQmFzZTY0IjoiIn0seyJXb3Jrb3V0VHlwZUNvZGUiOiJUUkVBRE1JTEwiLCJEaXNwbGF5TmFtZSI6IlRyZWFkbWlsbCIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJDT05ESVRJT05JTkdfQ0FSRElPIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6IkNZQ0xJTkciLCJEaXNwbGF5TmFtZSI6IkN5Y2xpbmciLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiQ09ORElUSU9OSU5HX0NBUkRJTyIsIkRlZmF1bHRNaW51dGVzIjowLjAwLCJEZWZhdWx0UmVzdFNlYyI6MCwiUHJvZmlsZVZlcnNpb25Db2RlIjoiIiwiUHJvZmlsZVZlcnNpb25OdW1iZXIiOm51bGwsIlByb2ZpbGVKc29uQmFzZTY0IjoiIn0seyJXb3Jrb3V0VHlwZUNvZGUiOiJST1dJTkciLCJEaXNwbGF5TmFtZSI6IlJvd2luZyIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJDT05ESVRJT05JTkdfQ0FSRElPIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6IkVMTElQVElDQUwiLCJEaXNwbGF5TmFtZSI6IkVsbGlwdGljYWwiLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiQ09ORElUSU9OSU5HX0NBUkRJTyIsIkRlZmF1bHRNaW51dGVzIjowLjAwLCJEZWZhdWx0UmVzdFNlYyI6MCwiUHJvZmlsZVZlcnNpb25Db2RlIjoiIiwiUHJvZmlsZVZlcnNpb25OdW1iZXIiOm51bGwsIlByb2ZpbGVKc29uQmFzZTY0IjoiIn0seyJXb3Jrb3V0VHlwZUNvZGUiOiJTVEFJUl9DTElNQkVSIiwiRGlzcGxheU5hbWUiOiJTdGFpciBDbGltYmVyIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiU1dJTU1JTkciLCJEaXNwbGF5TmFtZSI6IlN3aW1taW5nIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiTUFKT1JfTEVHUyIsIkRpc3BsYXlOYW1lIjoiTWFqb3IgTGVncyIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJNQUpPUl9TVFJFTkdUSCIsIkRlZmF1bHRNaW51dGVzIjowLjAwLCJEZWZhdWx0UmVzdFNlYyI6MCwiUHJvZmlsZVZlcnNpb25Db2RlIjoiRTMwLU1BSk9SX0xFR1MtVjAwMDEiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6MSwiUHJvZmlsZUpzb25CYXNlNjQiOiJld0FOQUFvQUlBQWdBQ0lBY3dCakFHZ0FaUUJ0QUdFQVZnQmxBSElBY3dCcEFHOEFiZ0FpQURvQUlBQXhBQ3dBRFFBS0FDQUFJQUFpQUhjQWJ3QnlBR3NBYndCMUFIUUFSZ0JoQUcwQWFRQnNBSGtBUXdCdkFHUUFaUUFpQURvQUlBQWlBRklBUlFCVEFFa0FVd0JVQUVFQVRnQkRBRVVBSWdBc0FBMEFDZ0FnQUNBQUlnQmtBR1VBWmdCaEFIVUFiQUIwQUUwQWJ3QmtBR0VBYkFCcEFIUUFlUUJEQUc4QVpBQmxBQ0lBT2dBZ0FDSUFVZ0JGQUZNQVNRQlRBRlFBUVFCT0FFTUFSUUFpQUN3QURRQUtBQ0FBSUFBaUFIUUFjZ0JoQUdrQWJnQnBBRzRBWndCUUFHRUFkQUIwQUdVQWNnQnVBRU1BYndCa0FHVUFJZ0E2QUNBQUlnQlNBRVVBVXdCSkFGTUFWQUJCQUU0QVF3QkZBRjhBVFFCQkFFb0FUd0JTQUNJQUxBQU5BQW9BSUFBZ0FDSUFjd0JsQUhNQWN3QnBBRzhBYmdCVEFIUUFjZ0IxQUdNQWRBQjFBSElBWlFCREFHOEFaQUJsQUNJQU9nQWdBQ0lBVWdCRkFGTUFTUUJUQUZRQVFRQk9BRU1BUlFCZkFGTUFSUUJVQUZNQUlnQXNBQTBBQ2dBZ0FDQUFJZ0JrQUdVQVpnQmhBSFVBYkFCMEFFUUFkUUJ5QUdFQWRBQnBBRzhBYmdCTkFHa0FiZ0IxQUhRQVpRQnpBQ0lBT2dBZ0FEWUFNQUFzQUEwQUNnQWdBQ0FBSWdCa0FHVUFaZ0JoQUhVQWJBQjBBRklBY0FCbEFDSUFPZ0FnQURnQUxBQU5BQW9BSUFBZ0FDSUFjd0IxQUhBQWNBQnZBSElBZEFCekFFOEFZZ0JxQUdVQVl3QjBBR2tBZGdCbEFGY0Fid0J5QUdzQWJBQnZBR0VBWkFBaUFEb0FJQUJtQUdFQWJBQnpBR1VBTEFBTkFBb0FJQUFnQUNJQWN3QjFBSEFBY0FCdkFISUFkQUJ6QUZJQVpRQnpBR2tBY3dCMEFHRUFiZ0JqQUdVQVJBQmxBSFFBWVFCcEFHd0Fjd0FpQURvQUlBQjBBSElBZFFCbEFDd0FEUUFLQUNBQUlBQWlBSE1BZFFCd0FIQUFid0J5QUhRQWN3QkpBRzRBZEFCbEFISUFkZ0JoQUd3QVJBQmxBSFFBWVFCcEFHd0Fjd0FpQURvQUlBQm1BR0VBYkFCekFHVUFMQUFOQUFvQUlBQWdBQ0lBY3dCMUFIQUFjQUJ2QUhJQWRBQnpBRk1BWlFCbkFHMEFaUUJ1QUhRQWN3QWlBRG9BSUFCbUFHRUFiQUJ6QUdVQUxBQU5BQW9BSUFBZ0FDSUFjZ0JsQUdZQVpRQnlBR1VBYmdCakFHVUFWd0J2QUhJQWF3QnZBSFVBZEFCREFHRUFiQUJ2QUhJQWFRQmxBSE1BVUFCbEFISUFUUUJwQUc0QWRRQjBBR1VBSWdBNkFDQUFOZ0F1QURBQU5RQXdBQ3dBRFFBS0FDQUFJQUFpQUhJQVpRQm1BR1VBY2dCbEFHNEFZd0JsQUZJQWNBQmxBQ0lBT2dBZ0FEZ0FMQUFOQUFvQUlBQWdBQ0lBY2dCbEFHWUFaUUJ5QUdVQWJnQmpBR1VBVndCbEFHa0Fad0JvQUhRQVRBQmlBQ0lBT2dBZ0FERUFPQUF3QUN3QURRQUtBQ0FBSUFBaUFITUFZd0JoQUd3QWFRQnVBR2NBUWdCaEFITUFhUUJ6QUVNQWJ3QmtBR1VBSWdBNkFDQUFJZ0JHQUVFQVZBQmZBRVlBVWdCRkFFVUFYd0JOQUVFQVV3QlRBQ0lBTEFBTkFBb0FJQUFnQUNJQWNnQmxBR1lBWlFCeUFHVUFiZ0JqQUdVQVJnQmhBSFFBUmdCeUFHVUFaUUJOQUdFQWN3QnpBRXdBWWdBaUFEb0FJQUF4QURNQU1BQXNBQTBBQ2dBZ0FDQUFJZ0JrQUdVQVpnQmhBSFVBYkFCMEFGSUFaUUJ6QUhRQVV3QmxBR01BYndCdUFHUUFjd0FpQURvQUlBQTVBREFBTEFBTkFBb0FJQUFnQUNJQWNnQmxBSE1BYVFCekFIUUFZUUJ1QUdNQVpRQlNBR1VBWmdCbEFISUFaUUJ1QUdNQVpRQkVBSFVBY2dCaEFIUUFhUUJ2QUc0QVRRQnBBRzRBZFFCMEFHVUFjd0FpQURvQUlBQTJBREFBTEFBTkFBb0FJQUFnQUNJQWNnQmxBSE1BYVFCekFIUUFZUUJ1QUdNQVpRQlNBR1VBWmdCbEFISUFaUUJ1QUdNQVpRQklBR0VBY2dCa0FGTUFaUUIwQUhNQUlnQTZBQ0FBTVFBMkFDd0FEUUFLQUNBQUlBQWlBSElBWlFCekFHa0Fjd0IwQUdFQWJnQmpBR1VBU0FCaEFISUFaQUJUQUdVQWRBQkVBR1VBYmdCekFHa0FkQUI1QUZNQVpRQnVBSE1BYVFCMEFHa0FkZ0JwQUhRQWVRQWlBRG9BSUFBd0FDNEFOUUF3QUN3QURRQUtBQ0FBSUFBaUFISUFaUUJ6QUdrQWN3QjBBR0VBYmdCakFHVUFVZ0JsQUhNQWRBQkdBR0VBYkFCc0FHSUFZUUJqQUdzQVV3QmxBRzRBY3dCcEFIUUFhUUIyQUdrQWRBQjVBQ0lBT2dBZ0FEQUFMZ0F5QURVQUxBQU5BQW9BSUFBZ0FDSUFjZ0JsQUhNQWFRQnpBSFFBWVFCdUFHTUFaUUJFQUdVQWJnQnpBR2tBZEFCNUFFMEFhUUJ1QUdrQWJRQjFBRzBBUmdCaEFHTUFkQUJ2QUhJQUlnQTZBQ0FBTUFBdUFEZ0FOUUFzQUEwQUNnQWdBQ0FBSWdCeUFHVUFjd0JwQUhNQWRBQmhBRzRBWXdCbEFFUUFaUUJ1QUhNQWFRQjBBSGtBVFFCaEFIZ0FhUUJ0QUhVQWJRQkdBR0VBWXdCMEFHOEFjZ0FpQURvQUlBQXhBQzRBTVFBMUFDd0FEUUFLQUNBQUlBQWlBR2NBYkFCNUFHTUFid0JuQUdVQWJnQk5BRzhBWkFCbEFHd0FRd0J2QUdRQVpRQWlBRG9BSUFBaUFGSUFSUUJUQUVrQVV3QlVBRUVBVGdCREFFVUFJZ0FzQUEwQUNnQWdBQ0FBSWdCbkFHd0FlUUJqQUc4QVp3QmxBRzRBVWdCbEFHWUFaUUJ5QUdVQWJnQmpBR1VBUkFCMUFISUFZUUIwQUdrQWJ3QnVBRTBBYVFCdUFIVUFkQUJsQUhNQUlnQTZBQ0FBTmdBd0FDd0FEUUFLQUNBQUlBQWlBR2NBYkFCNUFHTUFid0JuQUdVQWJnQlNBR1VBWmdCbEFISUFaUUJ1QUdNQVpRQkVBR1VBY0FCc0FHVUFkQUJwQUc4QWJnQkdBSElBWVFCakFIUUFhUUJ2QUc0QUlnQTZBQ0FBTUFBdUFESUFPQUFzQUEwQUNnQWdBQ0FBSWdCbkFHd0FlUUJqQUc4QVp3QmxBRzRBU1FCdUFIUUFaUUJ1QUhNQWFRQjBBSGtBVXdCbEFHNEFjd0JwQUhRQWFRQjJBR2tBZEFCNUFDSUFPZ0FnQURBQUxnQTFBREFBTEFBTkFBb0FJQUFnQUNJQVp3QnNBSGtBWXdCdkFHY0FaUUJ1QUUwQWFRQnVBR2tBYlFCMUFHMEFSQUJsQUhBQWJBQmxBSFFBYVFCdkFHNEFSZ0J5QUdFQVl3QjBBR2tBYndCdUFDSUFPZ0FnQURBQUxnQXdBRFVBTEFBTkFBb0FJQUFnQUNJQVp3QnNBSGtBWXdCdkFHY0FaUUJ1QUUwQVlRQjRBR2tBYlFCMUFHMEFSQUJsQUhBQWJBQmxBSFFBYVFCdkFHNEFSZ0J5QUdFQVl3QjBBR2tBYndCdUFDSUFPZ0FnQURBQUxnQTJBRFVBTEFBTkFBb0FJQUFnQUNJQWRBQmhBSElBWndCbEFIUUFVZ0JsQUhNQWRBQnZBSElBWVFCMEFHa0Fid0J1QUVZQWNnQmhBR01BZEFCcEFHOEFiZ0FpQURvQUlBQXhBQzRBTUFBd0FDd0FEUUFLQUNBQUlBQWlBR01BWVFCeUFHSUFid0JvQUhrQVpBQnlBR0VBZEFCbEFGQUFaUUJ5QUVjQWJBQjVBR01BYndCbkFHVUFiZ0JIQUNJQU9nQWdBREVBTGdBd0FEQUFMQUFOQUFvQUlBQWdBQ0lBYVFCdUFIUUFaUUJ5QUhZQVlRQnNBRmNBYndCeUFHc0FVd0JsQUdNQWJ3QnVBR1FBY3dBaUFEb0FJQUEyQURBQUxBQU5BQW9BSUFBZ0FDSUFhUUJ1QUhRQVpRQnlBSFlBWVFCc0FGSUFaUUJqQUc4QWRnQmxBSElBZVFCVEFHVUFZd0J2QUc0QVpBQnpBQ0lBT2dBZ0FEWUFNQUFzQUEwQUNnQWdBQ0FBSWdCcEFHNEFkQUJsQUhJQWRnQmhBR3dBVWdCbEFIQUFaUUIwQUdrQWRBQnBBRzhBYmdCekFDSUFPZ0FnQURFQU5RQXNBQTBBQ2dBZ0FDQUFJZ0JwQUc0QWRBQmxBSElBZGdCaEFHd0FWd0J2QUhJQWF3QlNBSEFBWlFBaUFEb0FJQUE0QUN3QURRQUtBQ0FBSUFBaUFHa0FiZ0IwQUdVQWNnQjJBR0VBYkFCU0FHVUFZd0J2QUhZQVpRQnlBSGtBVWdCd0FHVUFJZ0E2QUNBQU13QXNBQTBBQ2dBZ0FDQUFJZ0JwQUc0QWRBQmxBSElBZGdCaEFHd0FVZ0JsQUdNQWJ3QjJBR1VBY2dCNUFFTUFid0J1QUhRQWNnQnBBR0lBZFFCMEFHa0Fid0J1QUZjQVpRQnBBR2NBYUFCMEFDSUFPZ0FnQURBQUxnQXlBRFVBTEFBTkFBb0FJQUFnQUNJQWFRQnVBSFFBWlFCeUFIWUFZUUJzQUZjQWJ3QnlBR3NBU1FCdUFIUUFaUUJ1QUhNQWFRQjBBSGtBVXdCbEFHNEFjd0JwQUhRQWFRQjJBR2tBZEFCNUFDSUFPZ0FnQURBQUxnQTFBREFBTEFBTkFBb0FJQUFnQUNJQWFRQnVBSFFBWlFCeUFIWUFZUUJzQUZJQVpRQmpBRzhBZGdCbEFISUFlUUJKQUc0QWRBQmxBRzRBY3dCcEFIUUFlUUJUQUdVQWJnQnpBR2tBZEFCcEFIWUFhUUIwQUhrQUlnQTZBQ0FBTUFBdUFESUFOUUFzQUEwQUNnQWdBQ0FBSWdCdEFIVUFjd0JqQUd3QVpRQlNBR1VBWndCcEFHOEFiZ0J6QUNJQU9nQWdBRnNBRFFBS0FDQUFJQUFnQUNBQWV3QU5BQW9BSUFBZ0FDQUFJQUFnQUNBQUlnQnRBSFVBY3dCakFHd0FaUUJTQUdVQVp3QnBBRzhBYmdCREFHOEFaQUJsQUNJQU9nQWdBQ0lBVEFCUEFGY0FSUUJTQUY4QVFnQlBBRVFBV1FBaUFDd0FEUUFLQUNBQUlBQWdBQ0FBSUFBZ0FDSUFkd0JvQUc4QWJBQmxBRUlBYndCa0FIa0FUUUIxQUhNQVl3QnNBR1VBUmdCeUFHRUFZd0IwQUdrQWJ3QnVBQ0lBT2dBZ0FEQUFMZ0ExQURBQUxBQU5BQW9BSUFBZ0FDQUFJQUFnQUNBQUlnQnlBR1VBWXdCeUFIVUFhUUIwQUcwQVpRQnVBSFFBUmdCeUFHRUFZd0IwQUdrQWJ3QnVBQ0lBT2dBZ0FEQUFMZ0E0QURVQUxBQU5BQW9BSUFBZ0FDQUFJQUFnQUNBQUlnQm9BR0VBY2dCa0FGTUFaUUIwQUhNQUlnQTZBQ0FBT0FBTkFBb0FJQUFnQUNBQUlBQjlBQTBBQ2dBZ0FDQUFYUUFzQUEwQUNnQWdBQ0FBSWdCekFHVUFad0J0QUdVQWJnQjBBSE1BSWdBNkFDQUFXd0JkQUEwQUNnQjlBQT09In0seyJXb3Jrb3V0VHlwZUNvZGUiOiJNQUpPUl9QVVNIIiwiRGlzcGxheU5hbWUiOiJNYWpvciBQdXNoIiwiV29ya291dENhdGVnb3J5Q29kZSI6Ik1BSk9SX1NUUkVOR1RIIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6Ik1BSk9SX1BVTEwiLCJEaXNwbGF5TmFtZSI6Ik1ham9yIFB1bGwiLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiTUFKT1JfU1RSRU5HVEgiLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiRlVMTF9CT0RZX1NUUkVOR1RIIiwiRGlzcGxheU5hbWUiOiJGdWxsLUJvZHkgU3RyZW5ndGgiLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiTUFKT1JfU1RSRU5HVEgiLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiQUNDRVNTT1JZIiwiRGlzcGxheU5hbWUiOiJBY2Nlc3NvcnkgLyBJc29sYXRpb24iLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiQUNDRVNTT1JZX1NUUkVOR1RIIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6IlJFU0lTVEFOQ0VfQ0lSQ1VJVCIsIkRpc3BsYXlOYW1lIjoiUmVzaXN0YW5jZSBDaXJjdWl0IiwiV29ya291dENhdGVnb3J5Q29kZSI6IkFDQ0VTU09SWV9TVFJFTkdUSCIsIkRlZmF1bHRNaW51dGVzIjowLjAwLCJEZWZhdWx0UmVzdFNlYyI6MCwiUHJvZmlsZVZlcnNpb25Db2RlIjoiRTMwLVJFU0lTVEFOQ0VfQ0lSQ1VJVC1WMDAwMSIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjoxLCJQcm9maWxlSnNvbkJhc2U2NCI6ImV3QU5BQW9BSUFBZ0FDSUFjd0JqQUdnQVpRQnRBR0VBVmdCbEFISUFjd0JwQUc4QWJnQWlBRG9BSUFBeEFDd0FEUUFLQUNBQUlBQWlBSGNBYndCeUFHc0Fid0IxQUhRQVJnQmhBRzBBYVFCc0FIa0FRd0J2QUdRQVpRQWlBRG9BSUFBaUFGSUFSUUJUQUVrQVV3QlVBRUVBVGdCREFFVUFJZ0FzQUEwQUNnQWdBQ0FBSWdCa0FHVUFaZ0JoQUhVQWJBQjBBRTBBYndCa0FHRUFiQUJwQUhRQWVRQkRBRzhBWkFCbEFDSUFPZ0FnQUNJQVVnQkZBRk1BU1FCVEFGUUFRUUJPQUVNQVJRQWlBQ3dBRFFBS0FDQUFJQUFpQUhRQWNnQmhBR2tBYmdCcEFHNEFad0JRQUdFQWRBQjBBR1VBY2dCdUFFTUFid0JrQUdVQUlnQTZBQ0FBSWdCU0FFVUFVd0JKQUZNQVZBQkJBRTRBUXdCRkFGOEFRd0JKQUZJQVF3QlZBRWtBVkFBaUFDd0FEUUFLQUNBQUlBQWlBSE1BWlFCekFITUFhUUJ2QUc0QVV3QjBBSElBZFFCakFIUUFkUUJ5QUdVQVF3QnZBR1FBWlFBaUFEb0FJQUFpQUZJQVJRQlRBRWtBVXdCVUFFRUFUZ0JEQUVVQVh3QlRBRVVBVkFCVEFDSUFMQUFOQUFvQUlBQWdBQ0lBWkFCbEFHWUFZUUIxQUd3QWRBQkVBSFVBY2dCaEFIUUFhUUJ2QUc0QVRRQnBBRzRBZFFCMEFHVUFjd0FpQURvQUlBQTJBREFBTEFBTkFBb0FJQUFnQUNJQVpBQmxBR1lBWVFCMUFHd0FkQUJTQUhBQVpRQWlBRG9BSUFBM0FDd0FEUUFLQUNBQUlBQWlBSE1BZFFCd0FIQUFid0J5QUhRQWN3QlBBR0lBYWdCbEFHTUFkQUJwQUhZQVpRQlhBRzhBY2dCckFHd0Fid0JoQUdRQUlnQTZBQ0FBWmdCaEFHd0Fjd0JsQUN3QURRQUtBQ0FBSUFBaUFITUFkUUJ3QUhBQWJ3QnlBSFFBY3dCU0FHVUFjd0JwQUhNQWRBQmhBRzRBWXdCbEFFUUFaUUIwQUdFQWFRQnNBSE1BSWdBNkFDQUFkQUJ5QUhVQVpRQXNBQTBBQ2dBZ0FDQUFJZ0J6QUhVQWNBQndBRzhBY2dCMEFITUFTUUJ1QUhRQVpRQnlBSFlBWVFCc0FFUUFaUUIwQUdFQWFRQnNBSE1BSWdBNkFDQUFkQUJ5QUhVQVpRQXNBQTBBQ2dBZ0FDQUFJZ0J6QUhVQWNBQndBRzhBY2dCMEFITUFVd0JsQUdjQWJRQmxBRzRBZEFCekFDSUFPZ0FnQUdZQVlRQnNBSE1BWlFBc0FBMEFDZ0FnQUNBQUlnQnlBR1VBWmdCbEFISUFaUUJ1QUdNQVpRQlhBRzhBY2dCckFHOEFkUUIwQUVNQVlRQnNBRzhBY2dCcEFHVUFjd0JRQUdVQWNnQk5BR2tBYmdCMUFIUUFaUUFpQURvQUlBQTFBQzRBTlFBc0FBMEFDZ0FnQUNBQUlnQnlBR1VBWmdCbEFISUFaUUJ1QUdNQVpRQlNBSEFBWlFBaUFEb0FJQUEzQUN3QURRQUtBQ0FBSUFBaUFISUFaUUJtQUdVQWNnQmxBRzRBWXdCbEFGY0FaUUJwQUdjQWFBQjBBRXdBWWdBaUFEb0FJQUF4QURnQU1BQXNBQTBBQ2dBZ0FDQUFJZ0J6QUdNQVlRQnNBR2tBYmdCbkFFSUFZUUJ6QUdrQWN3QkRBRzhBWkFCbEFDSUFPZ0FnQUNJQVJnQkJBRlFBWHdCR0FGSUFSUUJGQUY4QVRRQkJBRk1BVXdBaUFDd0FEUUFLQUNBQUlBQWlBSElBWlFCbUFHVUFjZ0JsQUc0QVl3QmxBRVlBWVFCMEFFWUFjZ0JsQUdVQVRRQmhBSE1BY3dCTUFHSUFJZ0E2QUNBQU1RQXpBREFBTEFBTkFBb0FJQUFnQUNJQVpBQmxBR1lBWVFCMUFHd0FkQUJTQUdVQWN3QjBBRk1BWlFCakFHOEFiZ0JrQUhNQUlnQTZBQ0FBT1FBd0FDd0FEUUFLQUNBQUlBQWlBSElBWlFCekFHa0Fjd0IwQUdFQWJnQmpBR1VBVWdCbEFHWUFaUUJ5QUdVQWJnQmpBR1VBUkFCMUFISUFZUUIwQUdrQWJ3QnVBRTBBYVFCdUFIVUFkQUJsQUhNQUlnQTZBQ0FBTmdBd0FDd0FEUUFLQUNBQUlBQWlBSElBWlFCekFHa0Fjd0IwQUdFQWJnQmpBR1VBVWdCbEFHWUFaUUJ5QUdVQWJnQmpBR1VBU0FCaEFISUFaQUJUQUdVQWRBQnpBQ0lBT2dBZ0FERUFOZ0FzQUEwQUNnQWdBQ0FBSWdCeUFHVUFjd0JwQUhNQWRBQmhBRzRBWXdCbEFFZ0FZUUJ5QUdRQVV3QmxBSFFBUkFCbEFHNEFjd0JwQUhRQWVRQlRBR1VBYmdCekFHa0FkQUJwQUhZQWFRQjBBSGtBSWdBNkFDQUFNQUF1QURVQU1BQXNBQTBBQ2dBZ0FDQUFJZ0J5QUdVQWN3QnBBSE1BZEFCaEFHNEFZd0JsQUZJQVpRQnpBSFFBUmdCaEFHd0FiQUJpQUdFQVl3QnJBRk1BWlFCdUFITUFhUUIwQUdrQWRnQnBBSFFBZVFBaUFEb0FJQUF3QUM0QU1nQTFBQ3dBRFFBS0FDQUFJQUFpQUhJQVpRQnpBR2tBY3dCMEFHRUFiZ0JqQUdVQVJBQmxBRzRBY3dCcEFIUUFlUUJOQUdrQWJnQnBBRzBBZFFCdEFFWUFZUUJqQUhRQWJ3QnlBQ0lBT2dBZ0FEQUFMZ0E0QURVQUxBQU5BQW9BSUFBZ0FDSUFjZ0JsQUhNQWFRQnpBSFFBWVFCdUFHTUFaUUJFQUdVQWJnQnpBR2tBZEFCNUFFMEFZUUI0QUdrQWJRQjFBRzBBUmdCaEFHTUFkQUJ2QUhJQUlnQTZBQ0FBTVFBdUFERUFOUUFzQUEwQUNnQWdBQ0FBSWdCbkFHd0FlUUJqQUc4QVp3QmxBRzRBVFFCdkFHUUFaUUJzQUVNQWJ3QmtBR1VBSWdBNkFDQUFJZ0JTQUVVQVV3QkpBRk1BVkFCQkFFNEFRd0JGQUNJQUxBQU5BQW9BSUFBZ0FDSUFad0JzQUhrQVl3QnZBR2NBWlFCdUFGSUFaUUJtQUdVQWNnQmxBRzRBWXdCbEFFUUFkUUJ5QUdFQWRBQnBBRzhBYmdCTkFHa0FiZ0IxQUhRQVpRQnpBQ0lBT2dBZ0FEWUFNQUFzQUEwQUNnQWdBQ0FBSWdCbkFHd0FlUUJqQUc4QVp3QmxBRzRBVWdCbEFHWUFaUUJ5QUdVQWJnQmpBR1VBUkFCbEFIQUFiQUJsQUhRQWFRQnZBRzRBUmdCeUFHRUFZd0IwQUdrQWJ3QnVBQ0lBT2dBZ0FEQUFMZ0F5QURnQUxBQU5BQW9BSUFBZ0FDSUFad0JzQUhrQVl3QnZBR2NBWlFCdUFFa0FiZ0IwQUdVQWJnQnpBR2tBZEFCNUFGTUFaUUJ1QUhNQWFRQjBBR2tBZGdCcEFIUUFlUUFpQURvQUlBQXdBQzRBTlFBd0FDd0FEUUFLQUNBQUlBQWlBR2NBYkFCNUFHTUFid0JuQUdVQWJnQk5BR2tBYmdCcEFHMEFkUUJ0QUVRQVpRQndBR3dBWlFCMEFHa0Fid0J1QUVZQWNnQmhBR01BZEFCcEFHOEFiZ0FpQURvQUlBQXdBQzRBTUFBMUFDd0FEUUFLQUNBQUlBQWlBR2NBYkFCNUFHTUFid0JuQUdVQWJnQk5BR0VBZUFCcEFHMEFkUUJ0QUVRQVpRQndBR3dBWlFCMEFHa0Fid0J1QUVZQWNnQmhBR01BZEFCcEFHOEFiZ0FpQURvQUlBQXdBQzRBTmdBMUFDd0FEUUFLQUNBQUlBQWlBSFFBWVFCeUFHY0FaUUIwQUZJQVpRQnpBSFFBYndCeUFHRUFkQUJwQUc4QWJnQkdBSElBWVFCakFIUUFhUUJ2QUc0QUlnQTZBQ0FBTVFBdUFEQUFNQUFzQUEwQUNnQWdBQ0FBSWdCakFHRUFjZ0JpQUc4QWFBQjVBR1FBY2dCaEFIUUFaUUJRQUdVQWNnQkhBR3dBZVFCakFHOEFad0JsQUc0QVJ3QWlBRG9BSUFBeEFDNEFNQUF3QUN3QURRQUtBQ0FBSUFBaUFHa0FiZ0IwQUdVQWNnQjJBR0VBYkFCWEFHOEFjZ0JyQUZNQVpRQmpBRzhBYmdCa0FITUFJZ0E2QUNBQU5nQXdBQ3dBRFFBS0FDQUFJQUFpQUdrQWJnQjBBR1VBY2dCMkFHRUFiQUJTQUdVQVl3QnZBSFlBWlFCeUFIa0FVd0JsQUdNQWJ3QnVBR1FBY3dBaUFEb0FJQUEyQURBQUxBQU5BQW9BSUFBZ0FDSUFhUUJ1QUhRQVpRQnlBSFlBWVFCc0FGSUFaUUJ3QUdVQWRBQnBBSFFBYVFCdkFHNEFjd0FpQURvQUlBQXhBRFVBTEFBTkFBb0FJQUFnQUNJQWFRQnVBSFFBWlFCeUFIWUFZUUJzQUZjQWJ3QnlBR3NBVWdCd0FHVUFJZ0E2QUNBQU9BQXNBQTBBQ2dBZ0FDQUFJZ0JwQUc0QWRBQmxBSElBZGdCaEFHd0FVZ0JsQUdNQWJ3QjJBR1VBY2dCNUFGSUFjQUJsQUNJQU9nQWdBRE1BTEFBTkFBb0FJQUFnQUNJQWFRQnVBSFFBWlFCeUFIWUFZUUJzQUZJQVpRQmpBRzhBZGdCbEFISUFlUUJEQUc4QWJnQjBBSElBYVFCaUFIVUFkQUJwQUc4QWJnQlhBR1VBYVFCbkFHZ0FkQUFpQURvQUlBQXdBQzRBTWdBMUFDd0FEUUFLQUNBQUlBQWlBR2tBYmdCMEFHVUFjZ0IyQUdFQWJBQlhBRzhBY2dCckFFa0FiZ0IwQUdVQWJnQnpBR2tBZEFCNUFGTUFaUUJ1QUhNQWFRQjBBR2tBZGdCcEFIUUFlUUFpQURvQUlBQXdBQzRBTlFBd0FDd0FEUUFLQUNBQUlBQWlBR2tBYmdCMEFHVUFjZ0IyQUdFQWJBQlNBR1VBWXdCdkFIWUFaUUJ5QUhrQVNRQnVBSFFBWlFCdUFITUFhUUIwQUhrQVV3QmxBRzRBY3dCcEFIUUFhUUIyQUdrQWRBQjVBQ0lBT2dBZ0FEQUFMZ0F5QURVQUxBQU5BQW9BSUFBZ0FDSUFiUUIxQUhNQVl3QnNBR1VBVWdCbEFHY0FhUUJ2QUc0QWN3QWlBRG9BSUFCYkFBMEFDZ0FnQUNBQUlBQWdBSHNBRFFBS0FDQUFJQUFnQUNBQUlBQWdBQ0lBYlFCMUFITUFZd0JzQUdVQVVnQmxBR2NBYVFCdkFHNEFRd0J2QUdRQVpRQWlBRG9BSUFBaUFFd0FUd0JYQUVVQVVnQmZBRUlBVHdCRUFGa0FJZ0FzQUEwQUNnQWdBQ0FBSUFBZ0FDQUFJQUFpQUhjQWFBQnZBR3dBWlFCQ0FHOEFaQUI1QUUwQWRRQnpBR01BYkFCbEFFWUFjZ0JoQUdNQWRBQnBBRzhBYmdBaUFEb0FJQUF3QUM0QU5RQXdBQ3dBRFFBS0FDQUFJQUFnQUNBQUlBQWdBQ0lBY2dCbEFHTUFjZ0IxQUdrQWRBQnRBR1VBYmdCMEFFWUFjZ0JoQUdNQWRBQnBBRzhBYmdBaUFEb0FJQUF3QUM0QU5nQTFBQ3dBRFFBS0FDQUFJQUFnQUNBQUlBQWdBQ0lBYUFCaEFISUFaQUJUQUdVQWRBQnpBQ0lBT2dBZ0FEWUFEUUFLQUNBQUlBQWdBQ0FBZlFBc0FBMEFDZ0FnQUNBQUlBQWdBSHNBRFFBS0FDQUFJQUFnQUNBQUlBQWdBQ0lBYlFCMUFITUFZd0JzQUdVQVVnQmxBR2NBYVFCdkFHNEFRd0J2QUdRQVpRQWlBRG9BSUFBaUFGVUFVQUJRQUVVQVVnQmZBRUlBVHdCRUFGa0FJZ0FzQUEwQUNnQWdBQ0FBSUFBZ0FDQUFJQUFpQUhjQWFBQnZBR3dBWlFCQ0FHOEFaQUI1QUUwQWRRQnpBR01BYkFCbEFFWUFjZ0JoQUdNQWRBQnBBRzhBYmdBaUFEb0FJQUF3QUM0QU1nQXdBQ3dBRFFBS0FDQUFJQUFnQUNBQUlBQWdBQ0lBY2dCbEFHTUFjZ0IxQUdrQWRBQnRBR1VBYmdCMEFFWUFjZ0JoQUdNQWRBQnBBRzhBYmdBaUFEb0FJQUF3QUM0QU5nQTFBQ3dBRFFBS0FDQUFJQUFnQUNBQUlBQWdBQ0lBYUFCaEFISUFaQUJUQUdVQWRBQnpBQ0lBT2dBZ0FEWUFEUUFLQUNBQUlBQWdBQ0FBZlFBTkFBb0FJQUFnQUYwQUxBQU5BQW9BSUFBZ0FDSUFjd0JsQUdjQWJRQmxBRzRBZEFCekFDSUFPZ0FnQUZzQVhRQU5BQW9BZlFBPSJ9LHsiV29ya291dFR5cGVDb2RlIjoiUlVOTklOR19JTlRFUlZBTFMiLCJEaXNwbGF5TmFtZSI6IlJ1bm5pbmcgSW50ZXJ2YWxzIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiQ1lDTElOR19JTlRFUlZBTFMiLCJEaXNwbGF5TmFtZSI6IkN5Y2xpbmcgSW50ZXJ2YWxzIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiUk9XSU5HX0lOVEVSVkFMUyIsIkRpc3BsYXlOYW1lIjoiUm93aW5nIEludGVydmFscyIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJDT05ESVRJT05JTkdfQ0FSRElPIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6Ik1JWEVEX0NPTkRJVElPTklORyIsIkRpc3BsYXlOYW1lIjoiTWl4ZWQgQ29uZGl0aW9uaW5nIiwiV29ya291dENhdGVnb3J5Q29kZSI6IkNPTkRJVElPTklOR19DQVJESU8iLCJEZWZhdWx0TWludXRlcyI6MC4wMCwiRGVmYXVsdFJlc3RTZWMiOjAsIlByb2ZpbGVWZXJzaW9uQ29kZSI6IiIsIlByb2ZpbGVWZXJzaW9uTnVtYmVyIjpudWxsLCJQcm9maWxlSnNvbkJhc2U2NCI6IiJ9LHsiV29ya291dFR5cGVDb2RlIjoiVFJVRV9ISUlUIiwiRGlzcGxheU5hbWUiOiJISUlUIC8gU3ByaW50IEludGVydmFscyIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJDT05ESVRJT05JTkdfQ0FSRElPIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifSx7IldvcmtvdXRUeXBlQ29kZSI6Ik9SQU5HRVRIRU9SWSIsIkRpc3BsYXlOYW1lIjoiT3JhbmdldGhlb3J5IC8gTWl4ZWQgSW50ZXJ2YWwgQ2xhc3MiLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiQ09ORElUSU9OSU5HX0NBUkRJTyIsIkRlZmF1bHRNaW51dGVzIjowLjAwLCJEZWZhdWx0UmVzdFNlYyI6MCwiUHJvZmlsZVZlcnNpb25Db2RlIjoiIiwiUHJvZmlsZVZlcnNpb25OdW1iZXIiOm51bGwsIlByb2ZpbGVKc29uQmFzZTY0IjoiIn0seyJXb3Jrb3V0VHlwZUNvZGUiOiJNSVhFRF9HWU1fU0VTU0lPTiIsIkRpc3BsYXlOYW1lIjoiTWl4ZWQgR3ltIFNlc3Npb24iLCJXb3Jrb3V0Q2F0ZWdvcnlDb2RlIjoiQ09ORElUSU9OSU5HX0NBUkRJTyIsIkRlZmF1bHRNaW51dGVzIjowLjAwLCJEZWZhdWx0UmVzdFNlYyI6MCwiUHJvZmlsZVZlcnNpb25Db2RlIjoiIiwiUHJvZmlsZVZlcnNpb25OdW1iZXIiOm51bGwsIlByb2ZpbGVKc29uQmFzZTY0IjoiIn0seyJXb3Jrb3V0VHlwZUNvZGUiOiJDVVNUT01fQ09NUE9TSVRFIiwiRGlzcGxheU5hbWUiOiJDdXN0b20gTXVsdGktQmxvY2sgV29ya291dCIsIldvcmtvdXRDYXRlZ29yeUNvZGUiOiJDT05ESVRJT05JTkdfQ0FSRElPIiwiRGVmYXVsdE1pbnV0ZXMiOjAuMDAsIkRlZmF1bHRSZXN0U2VjIjowLCJQcm9maWxlVmVyc2lvbkNvZGUiOiIiLCJQcm9maWxlVmVyc2lvbk51bWJlciI6bnVsbCwiUHJvZmlsZUpzb25CYXNlNjQiOiIifV0=";

    private static readonly DateOnly WeekStart = new(2026, 9, 7);

    [Fact]
    public async Task FullPopulationAndWorkoutPlausibilityGate()
    {
        var fixtures = LoadFixtures();

        var hardFailures = new List<string>();
        var reviewFlags = new List<string>();
        var report = new StringBuilder();

        report.AppendLine("12 WEEK RECOMP - ENGINE 3.0 FULL PLAUSIBILITY VALIDATION");
        report.AppendLine($"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine("Configuration: CFG-3000.0");
        report.AppendLine("Population matrix: 10 synthetic people x 7 days = 70 rows");
        report.AppendLine("Workout sweep: all 27 Engine 3.0 workout presets");
        report.AppendLine();

        if (fixtures.Count != 27)
            hardFailures.Add($"Expected 27 workout fixtures; found {fixtures.Count}.");

        var provider = new FixtureProfileProvider(fixtures);
        var workoutRuntime = new Engine30WorkoutRuntimeService(provider);
        var dailyRuntime = new Engine30DailyRuntimeService(workoutRuntime);
        var prescriptionService = new Engine30PrescriptionService();
        var fuelStateService = new Engine30FuelStateService();
        var fuelProjectionService = new Engine30WeeklyFuelProjectionService(fuelStateService);

        await WriteWorkoutSweep(
            fixtures,
            workoutRuntime,
            hardFailures,
            reviewFlags,
            report);

        await WritePopulationMatrix(
            fixtures,
            dailyRuntime,
            prescriptionService,
            fuelStateService,
            fuelProjectionService,
            hardFailures,
            reviewFlags,
            report);

        await RunDirectionalChecks(
            dailyRuntime,
            prescriptionService,
            hardFailures,
            report);

        report.AppendLine();
        report.AppendLine("====================================================================================================================");
        report.AppendLine("FINAL VALIDATION SUMMARY");
        report.AppendLine("====================================================================================================================");
        report.AppendLine($"Hard invariant failures: {hardFailures.Count}");
        report.AppendLine($"Human-review flags:      {reviewFlags.Count}");

        if (hardFailures.Count == 0)
            report.AppendLine("AUTOMATED RESULT: PASS");
        else
            report.AppendLine("AUTOMATED RESULT: FAIL");

        if (reviewFlags.Count == 0)
            report.AppendLine("HUMAN PLAUSIBILITY: NO REVIEW FLAGS");
        else
            report.AppendLine("HUMAN PLAUSIBILITY: REVIEW FLAGGED ROWS BELOW");

        if (hardFailures.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("HARD FAILURES");
            foreach (var failure in hardFailures)
                report.AppendLine($"  FAIL: {failure}");
        }

        if (reviewFlags.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("REVIEW FLAGS");
            foreach (var flag in reviewFlags.Distinct())
                report.AppendLine($"  REVIEW: {flag}");
        }

        var reportPath =
            Environment.GetEnvironmentVariable("ENGINE30_VALIDATION_REPORT_PATH");

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(reportPath, report.ToString());
        }

        Assert.Empty(hardFailures);
    }

    private static async Task WriteWorkoutSweep(
        IReadOnlyList<WorkoutFixture> fixtures,
        Engine30WorkoutRuntimeService workoutRuntime,
        List<string> hardFailures,
        List<string> reviewFlags,
        StringBuilder report)
    {
        report.AppendLine("====================================================================================================================");
        report.AppendLine("SECTION A - 27-WORKOUT STANDARDIZED SWEEP");
        report.AppendLine("Reference: 200-lb male, 20% body fat, 60-minute session");
        report.AppendLine("====================================================================================================================");
        report.AppendLine(
            $"{"WORKOUT",-27} {"FAMILY",-11} {"RPE",5} {"GROSS",7} {"NET",7} {"MET",6} {"GLY(g)",7} {"GLY/E",6} {"PROFILE",-22} REVIEW");
        report.AppendLine(new string('-', 128));

        var baseLiving =
            new Engine30BaseLivingCalculator().Calculate(
                new Engine30BaseLivingInput(
                    ActionDate: WeekStart,
                    DateOfBirth: new DateOnly(1986, 1, 1),
                    SexCode: "MALE",
                    HeightIn: 72m,
                    WeightLb: 200m));

        const decimal weightLb = 200m;
        const decimal ffmLb = 160m;
        const decimal muscleFraction = 0.528m;
        const decimal baselineGlycogen = 23m;
        var weightKg = weightLb / 2.2046226218m;

        foreach (var fixture in fixtures)
        {
            try
            {
                var result =
                    await workoutRuntime.CalculateAsync(
                        new Engine30WorkoutRuntimeRequest(
                            Session:
                                new Engine30WorkoutSessionInput(
                                    WorkoutTypeCode: fixture.WorkoutTypeCode,
                                    PlannedDurationMinutes: 60m),
                            BaseLivingEnergyKcal: baseLiving.BaseLivingEnergyKcal,
                            WeightLb: weightLb,
                            FatFreeMassLb: ffmLb,
                            SkeletalMuscleFractionOfFfm: muscleFraction,
                            BaselineGlycogenGPerKgMuscle: baselineGlycogen));

                var gross = result.Energy.GrossWorkoutEnergyKcal;
                var net = result.Energy.EngineNetWorkoutEnergyKcal;
                var glycogen = result.Glycogen.EstimatedGlycogenDepletedG;
                var met = gross <= 0m ? 0m : gross / weightKg;
                var glyE = gross <= 0m ? 0m : glycogen * 4m / gross;

                if (gross < 0m)
                    hardFailures.Add($"{fixture.WorkoutTypeCode}: negative gross workout energy.");
                if (net < 0m)
                    hardFailures.Add($"{fixture.WorkoutTypeCode}: negative net workout energy.");
                if (glycogen < 0m)
                    hardFailures.Add($"{fixture.WorkoutTypeCode}: negative glycogen depletion.");
                if (net > gross + 0.05m)
                    hardFailures.Add($"{fixture.WorkoutTypeCode}: net energy exceeds gross energy.");
                if (result.Glycogen.CarbohydrateReloadRequirementG < 0m)
                    hardFailures.Add($"{fixture.WorkoutTypeCode}: negative carbohydrate reload.");

                var reasons = new List<string>();

                if (!fixture.WorkoutTypeCode.Equals("REST", StringComparison.OrdinalIgnoreCase)
                    && gross > 0m
                    && (met < 1.5m || met > 15m))
                {
                    reasons.Add("MET_RANGE");
                }

                if (gross > 1200m)
                    reasons.Add("ENERGY_HIGH");

                if (glycogen > 250m)
                    reasons.Add("GLYCOGEN_HIGH");

                if (gross > 0m && glyE > 2.0m)
                    reasons.Add("GLYCOGEN_TO_ENERGY_HIGH");

                if (reasons.Count > 0)
                {
                    reviewFlags.Add(
                        $"{fixture.WorkoutTypeCode} standardized sweep: {string.Join(",", reasons)} " +
                        $"(MET {met:0.00}, gross {gross:0.0}, glycogen {glycogen:0.0}g).");
                }

                var profile =
                    result.ProfileProvenance.SourceCode ==
                    Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile
                        ? result.ProfileProvenance.ProfileVersionCode ?? "ACTIVE"
                        : "BASELINE_FALLBACK";

                report.AppendLine(
                    $"{fixture.WorkoutTypeCode,-27} " +
                    $"{result.ResolvedSession.WorkoutFamilyCode,-11} " +
                    $"{result.ResolvedSession.AnalysisRpe.Value,5:0.0} " +
                    $"{gross,7:0.0} " +
                    $"{net,7:0.0} " +
                    $"{met,6:0.00} " +
                    $"{glycogen,7:0.0} " +
                    $"{glyE,6:0.00} " +
                    $"{profile,-22} " +
                    $"{(reasons.Count == 0 ? "OK" : string.Join(",", reasons))}");
            }
            catch (Exception ex)
            {
                hardFailures.Add(
                    $"{fixture.WorkoutTypeCode}: runtime exception {ex.GetType().Name}: {ex.Message}");

                report.AppendLine(
                    $"{fixture.WorkoutTypeCode,-27} ERROR: {ex.GetType().Name}: {ex.Message}");
            }
        }

        report.AppendLine();
    }

    private static async Task WritePopulationMatrix(
        IReadOnlyList<WorkoutFixture> fixtures,
        Engine30DailyRuntimeService dailyRuntime,
        Engine30PrescriptionService prescriptionService,
        Engine30FuelStateService fuelStateService,
        Engine30WeeklyFuelProjectionService fuelProjectionService,
        List<string> hardFailures,
        List<string> reviewFlags,
        StringBuilder report)
    {
        var fixtureByCode =
            fixtures.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        var people = People();

        var schedule = new string?[]
        {
            "MAJOR_LEGS",
            "ACCESSORY",
            "ZONE2_CARDIO",
            "MAJOR_PUSH",
            "RUNNING_INTERVALS",
            "ORANGETHEORY",
            null
        };

        report.AppendLine("====================================================================================================================");
        report.AppendLine("SECTION B - 10-PERSON / 70-DAY END-TO-END MATRIX");
        report.AppendLine("Columns: Base + Habitual + Net Workout = Maintenance -> Prescription -> Macros -> Fuel State");
        report.AppendLine("====================================================================================================================");
        report.AppendLine(
            $"{"PERSON",-15} {"DAY",-3} {"WORKOUT",-18} {"BASE",6} {"HAB",6} {"NET",6} {"MAINT",7} {"RX",6} " +
            $"{"P",4} {"C",4} {"F",4} {"GLY",6} {"IN",5} {"OUT",5} {"RESC",5} {"VAR",6} REVIEW");
        report.AppendLine(new string('-', 140));

        var matrixRows = 0;

        foreach (var person in people)
        {
            var daily = new List<Engine30DailyRuntimeResult>();

            for (var i = 0; i < 7; i++)
            {
                var code = schedule[i];

                Engine30WorkoutSessionInput? session =
                    code is null
                        ? null
                        : new Engine30WorkoutSessionInput(
                            WorkoutTypeCode: code,
                            PlannedDurationMinutes: 0m);

                try
                {
                    var result =
                        await dailyRuntime.CalculateAsync(
                            new Engine30DailyRuntimeRequest(
                                ActionDate: WeekStart.AddDays(i),
                                DateOfBirth: person.DateOfBirth,
                                SexCode: person.SexCode,
                                HeightIn: person.HeightIn,
                                WeightLb: person.WeightLb,
                                BodyFatFraction: person.BodyFatFraction,
                                OccupationalRoles: person.Roles,
                                LifestyleActivityCode: person.LifestyleActivityCode,
                                WorkoutSession: session));

                    daily.Add(result);

                    var recomposed =
                        Math.Round(
                            result.Energy.BaseLivingEnergyKcal
                            + result.Energy.HabitualActivityEnergyKcal
                            + result.Energy.NetWorkoutActivityEnergyKcal,
                            2,
                            MidpointRounding.AwayFromZero);

                    if (Math.Abs(recomposed - result.Energy.MaintenanceEnergyKcal) > 0.05m)
                    {
                        hardFailures.Add(
                            $"{person.Name} day {i + 1}: three-bucket maintenance reconciliation failed.");
                    }

                    if (result.Energy.BaseLivingEnergyKcal <= 0m)
                        hardFailures.Add($"{person.Name} day {i + 1}: non-positive Base Living.");
                    if (result.Energy.HabitualActivityEnergyKcal < 0m)
                        hardFailures.Add($"{person.Name} day {i + 1}: negative Habitual Activity.");
                    if (result.Energy.NetWorkoutActivityEnergyKcal < 0m)
                        hardFailures.Add($"{person.Name} day {i + 1}: negative Net Workout.");
                    if (result.Energy.MaintenanceEnergyKcal <= 0m)
                        hardFailures.Add($"{person.Name} day {i + 1}: non-positive Maintenance.");
                    if (result.Glycogen.ReloadEnergyIncludedInMaintenance)
                        hardFailures.Add($"{person.Name} day {i + 1}: glycogen diagnostic energy entered maintenance.");
                }
                catch (Exception ex)
                {
                    hardFailures.Add(
                        $"{person.Name} day {i + 1}: daily runtime exception {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (daily.Count != 7)
                continue;

            var prescriptionInputs =
                daily.Select((x, i) =>
                    new Engine30PrescriptionDayInput(
                        Date: x.ActionDate,
                        MaintenanceEnergyKcal: x.Energy.MaintenanceEnergyKcal,
                        WorkoutCategoryCode:
                            schedule[i] is null
                                ? Engine30PrescriptionCategoryCodes.RestRecoveryOther
                                : fixtureByCode[schedule[i]!].WorkoutCategoryCode,
                        GlycogenReloadRequirementG:
                            x.Glycogen.CarbohydrateReloadRequirementG))
                .ToArray();

            Engine30WeeklyPrescriptionResult prescription;

            try
            {
                prescription =
                    prescriptionService.Calculate(
                        person.PlanCode,
                        person.WeightLb,
                        person.HeightIn,
                        prescriptionInputs);
            }
            catch (Exception ex)
            {
                hardFailures.Add(
                    $"{person.Name}: prescription exception {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var prescribedSum = prescription.Days.Sum(x => x.PrescribedEnergyKcal);

            if (Math.Abs(prescribedSum - prescription.WeeklyPrescribedEnergyKcal) > 0.05m)
                hardFailures.Add($"{person.Name}: weekly prescribed energy does not reconcile.");

            if (prescription.EffectiveDeficitPct < 0m || prescription.EffectiveDeficitPct > 0.2001m)
                hardFailures.Add($"{person.Name}: effective deficit outside 0-20% guardrail.");

            if (prescription.WeeklyDeficitKcal > 3500.1m)
                hardFailures.Add($"{person.Name}: weekly deficit exceeds 3,500 kcal cap.");

            if (prescription.Days.Select(x => x.ProteinG).Distinct().Count() != 1)
                hardFailures.Add($"{person.Name}: protein is not stable across the week.");

            if (prescription.Days.Select(x => x.FatG).Distinct().Count() != 1)
                hardFailures.Add($"{person.Name}: fat is not stable across the week.");

            var weightKg = person.WeightLb / 2.2046226218m;

            if (prescription.StableFatG + 0.01m < weightKg * 0.50m)
                hardFailures.Add($"{person.Name}: fat fell below 0.5 g/kg floor.");

            var histStart = WeekStart.AddDays(-4);
            var historical =
                fuelStateService.BuildHistoricalWindow(
                new[]
                {
                    new Engine30ObservedFuelDayInput(
                        histStart,
                        55m,
                        prescription.BaseRestCarbsG,
                        prescription.BaseRestCarbsG + 30m),
                    new Engine30ObservedFuelDayInput(
                        histStart.AddDays(1),
                        85m,
                        prescription.BaseRestCarbsG,
                        prescription.BaseRestCarbsG + 45m),
                    new Engine30ObservedFuelDayInput(
                        histStart.AddDays(2),
                        0m,
                        prescription.BaseRestCarbsG,
                        prescription.BaseRestCarbsG),
                    new Engine30ObservedFuelDayInput(
                        histStart.AddDays(3),
                        70m,
                        prescription.BaseRestCarbsG,
                        prescription.BaseRestCarbsG + 35m)
                });

            var plannedFuel =
                daily.Select((x, i) =>
                    new Engine30PlannedFuelDay(
                        Date: x.ActionDate,
                        PlannedGlycogenDemandG: x.Glycogen.EstimatedGlycogenDepletedG,
                        PlannedRestorationFraction:
                            x.Glycogen.HasWorkout ? 0.80m : 0m,
                        MaxCarbohydrateAvailableBeforeWorkoutG:
                            x.Glycogen.HasWorkout ? 45m : 0m))
                .ToArray();

            Engine30WeeklyFuelProjectionResult fuelProjection;

            try
            {
                fuelProjection =
                    fuelProjectionService.Calculate(
                        historical,
                        prescription,
                        plannedFuel);
            }
            catch (Exception ex)
            {
                hardFailures.Add(
                    $"{person.Name}: fuel projection exception {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (fuelProjection.Days.Count != 7)
                hardFailures.Add($"{person.Name}: fuel projection did not return seven days.");

            for (var i = 0; i < 7; i++)
            {
                matrixRows++;

                var day = daily[i];
                var rx = prescription.Days.Single(x => x.Date == day.ActionDate);
                var fuel = fuelProjection.Days.Single(x => x.Date == day.ActionDate);
                var code = schedule[i] ?? "REST";

                if (rx.ProteinG < 0m || rx.FatG < 0m || rx.RecommendedCarbsG < 0m)
                    hardFailures.Add($"{person.Name} {day.ActionDate}: negative macro.");

                if (fuel.PreWorkoutCarbohydrateAllocationG < 0m)
                    hardFailures.Add($"{person.Name} {day.ActionDate}: negative rescue carbohydrate.");

                if (fuel.PreWorkoutCarbohydrateAllocationG > rx.RecommendedCarbsG + 0.05m)
                    hardFailures.Add($"{person.Name} {day.ActionDate}: rescue exceeds prescribed carbohydrate.");

                if (fuel.Readiness.IncomingAchievementRatio < 0m
                    || fuel.Readiness.IncomingAchievementRatio > 1.0001m
                    || fuel.Readiness.ProjectedAchievementRatio < 0m
                    || fuel.Readiness.ProjectedAchievementRatio > 1.0001m)
                {
                    hardFailures.Add($"{person.Name} {day.ActionDate}: fuel readiness ratio outside 0-1.");
                }

                var reasons = new List<string>();

                if (day.Energy.MaintenanceEnergyKcal > 4500m)
                    reasons.Add("MAINT_HIGH");

                if (rx.PrescribedEnergyKcal < 1200m)
                    reasons.Add("RX_LOW");

                if (rx.PrescribedEnergyKcal > 4500m)
                    reasons.Add("RX_HIGH");

                if (Math.Abs(rx.MacroEnergyVarianceKcal) > 100m)
                    reasons.Add("MACRO_VARIANCE");

                if (rx.RecommendedCarbsG > weightKg * 6.0m)
                    reasons.Add("CARBS_GT_6GKG");

                if (day.Glycogen.EstimatedGlycogenDepletedG > 250m)
                    reasons.Add("GLYCOGEN_HIGH");

                if (day.Energy.NetWorkoutActivityEnergyKcal > 1000m)
                    reasons.Add("WORKOUT_NET_HIGH");

                if (day.Energy.HabitualActivityEnergyKcal > 1200m)
                    reasons.Add("HABITUAL_HIGH");

                if (reasons.Count > 0)
                {
                    reviewFlags.Add(
                        $"{person.Name} {day.ActionDate:yyyy-MM-dd} {code}: {string.Join(",", reasons)}.");
                }

                report.AppendLine(
                    $"{person.Name,-15} " +
                    $"{i + 1,3} " +
                    $"{code,-18} " +
                    $"{day.Energy.BaseLivingEnergyKcal,6:0} " +
                    $"{day.Energy.HabitualActivityEnergyKcal,6:0} " +
                    $"{day.Energy.NetWorkoutActivityEnergyKcal,6:0} " +
                    $"{day.Energy.MaintenanceEnergyKcal,7:0} " +
                    $"{rx.PrescribedEnergyKcal,6:0} " +
                    $"{rx.ProteinG,4:0} " +
                    $"{rx.RecommendedCarbsG,4:0} " +
                    $"{rx.FatG,4:0} " +
                    $"{day.Glycogen.EstimatedGlycogenDepletedG,6:0.0} " +
                    $"{fuel.Readiness.IncomingAchievementRatio,5:0.00} " +
                    $"{fuel.Readiness.ProjectedAchievementRatio,5:0.00} " +
                    $"{fuel.PreWorkoutCarbohydrateAllocationG,5:0} " +
                    $"{rx.MacroEnergyVarianceKcal,6:0} " +
                    $"{(reasons.Count == 0 ? "OK" : string.Join(",", reasons))}");
            }

            report.AppendLine(
                $"  WEEK SUMMARY {person.Name}: BMI {prescription.Bmi:0.0}, " +
                $"maintenance {prescription.WeeklyMaintenanceEnergyKcal / 7m:0} kcal/day avg, " +
                $"prescribed {prescription.AverageDailyPrescribedEnergyKcal:0} kcal/day avg, " +
                $"effective deficit {prescription.EffectiveDeficitPct:P1}, " +
                $"P {prescription.StableProteinG:0}g / F {prescription.StableFatG:0}g, " +
                $"final fuel {fuelProjection.FinalProjectedWindow.AbsoluteRestorationRatio:P0}");
            report.AppendLine();
        }

        if (matrixRows != 70)
            hardFailures.Add($"Expected 70 population/day rows; produced {matrixRows}.");

        report.AppendLine($"Population matrix rows produced: {matrixRows}");
        report.AppendLine();
    }

    private static async Task RunDirectionalChecks(
        Engine30DailyRuntimeService dailyRuntime,
        Engine30PrescriptionService prescriptionService,
        List<string> hardFailures,
        StringBuilder report)
    {
        report.AppendLine("====================================================================================================================");
        report.AppendLine("SECTION C - DIRECTIONAL / SAFETY CHECKS");
        report.AppendLine("====================================================================================================================");

        var baseRequest =
            new Engine30DailyRuntimeRequest(
                ActionDate: WeekStart,
                DateOfBirth: new DateOnly(1986, 1, 1),
                SexCode: "MALE",
                HeightIn: 72m,
                WeightLb: 200m,
                BodyFatFraction: 0.20m,
                OccupationalRoles:
                    new[]
                    {
                        new Engine30OccupationalRoleInput(
                            "Desk",
                            "SEDENTARY",
                            40m,
                            0.20m)
                    },
                LifestyleActivityCode: "MINIMAL",
                WorkoutSession: null);

        var sedentary = await dailyRuntime.CalculateAsync(baseRequest);

        var active =
            await dailyRuntime.CalculateAsync(
                baseRequest with
                {
                    OccupationalRoles =
                        new[]
                        {
                            new Engine30OccupationalRoleInput(
                                "Construction",
                                "HEAVY",
                                40m,
                                1.00m)
                        },
                    LifestyleActivityCode = "MODERATE"
                });

        if (active.Energy.HabitualActivityEnergyKcal
            <= sedentary.Energy.HabitualActivityEnergyKcal)
        {
            hardFailures.Add("Directional check failed: heavier occupation did not increase Habitual Activity.");
        }

        if (active.Energy.MaintenanceEnergyKcal
            <= sedentary.Energy.MaintenanceEnergyKcal)
        {
            hardFailures.Add("Directional check failed: heavier occupation did not increase Maintenance.");
        }

        report.AppendLine(
            $"Habitual activity direction: sedentary {sedentary.Energy.HabitualActivityEnergyKcal:0} -> " +
            $"active {active.Energy.HabitualActivityEnergyKcal:0} kcal/day : " +
            $"{(active.Energy.HabitualActivityEnergyKcal > sedentary.Energy.HabitualActivityEnergyKcal ? "PASS" : "FAIL")}");

        var underweightDays =
            Enumerable.Range(0, 7)
                .Select(i =>
                    new Engine30PrescriptionDayInput(
                        WeekStart.AddDays(i),
                        1900m,
                        Engine30PrescriptionCategoryCodes.RestRecoveryOther,
                        0m))
                .ToArray();

        var underweight =
            prescriptionService.Calculate(
                "PLAN_2",
                weightLb: 110m,
                heightIn: 67m,
                days: underweightDays);

        if (underweight.Bmi >= 18.5m)
            hardFailures.Add("Underweight test fixture did not actually produce BMI < 18.5.");

        if (underweight.EffectiveDeficitPct != 0m)
            hardFailures.Add("BMI guardrail failed: underweight user received a deficit.");

        report.AppendLine(
            $"BMI deficit guardrail: BMI {underweight.Bmi:0.0}, effective deficit {underweight.EffectiveDeficitPct:P1} : " +
            $"{(underweight.EffectiveDeficitPct == 0m ? "PASS" : "FAIL")}");

        report.AppendLine();
    }

    private static IReadOnlyList<PersonScenario> People()
        => new[]
        {
            new PersonScenario(
                "M25_DESK",
                new DateOnly(2001, 1, 1),
                "MALE",
                68m,
                155m,
                0.15m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Office", "SEDENTARY", 40m, 0.20m)
                },
                "MINIMAL",
                "PLAN_2"),

            new PersonScenario(
                "F35_DESK",
                new DateOnly(1991, 1, 1),
                "FEMALE",
                64m,
                135m,
                0.25m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Office", "SEDENTARY", 40m, 0.20m)
                },
                "LIGHT",
                "PLAN_2"),

            new PersonScenario(
                "M45_LIGHT",
                new DateOnly(1981, 1, 1),
                "MALE",
                70m,
                190m,
                0.22m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Technician", "LIGHT", 40m, 0.40m)
                },
                "LIGHT",
                "PLAN_1"),

            new PersonScenario(
                "F55_TEACH",
                new DateOnly(1971, 1, 1),
                "FEMALE",
                66m,
                175m,
                0.32m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Teacher", "LIGHT_STANDING", 35m, 0.45m)
                },
                "MODERATE",
                "PLAN_3"),

            new PersonScenario(
                "M65_BUS",
                new DateOnly(1961, 1, 1),
                "MALE",
                73m,
                210m,
                0.25m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Bus Driver", "MOSTLY_SEATED", 25m, 0.25m)
                },
                "LIGHT",
                "PLAN_2"),

            new PersonScenario(
                "F30_NURSE",
                new DateOnly(1996, 1, 1),
                "FEMALE",
                69m,
                155m,
                0.22m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Nurse", "ACTIVE_STANDING", 36m, 0.60m)
                },
                "MODERATE",
                "PLAN_4"),

            new PersonScenario(
                "M40_CONST",
                new DateOnly(1986, 1, 1),
                "MALE",
                76m,
                250m,
                0.30m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Construction", "HEAVY", 40m, 1.00m)
                },
                "MODERATE",
                "PLAN_2"),

            new PersonScenario(
                "F60_RETAIL",
                new DateOnly(1966, 1, 1),
                "FEMALE",
                62m,
                190m,
                0.35m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Retail", "STANDING", 32m, 0.50m)
                },
                "LIGHT",
                "PLAN_1"),

            new PersonScenario(
                "M50_WARE",
                new DateOnly(1976, 1, 1),
                "MALE",
                66m,
                165m,
                0.18m,
                new[]
                {
                    new Engine30OccupationalRoleInput("Warehouse", "ACTIVE", 40m, 0.75m)
                },
                "LIGHT",
                "PLAN_3"),

            new PersonScenario(
                "F70_RET",
                new DateOnly(1956, 1, 1),
                "FEMALE",
                67m,
                145m,
                0.28m,
                Array.Empty<Engine30OccupationalRoleInput>(),
                "MODERATE",
                "PLAN_5")
        };

    private static IReadOnlyList<WorkoutFixture> LoadFixtures()
    {
        var json =
            Encoding.UTF8.GetString(
                Convert.FromBase64String(FixtureBase64));

        return JsonSerializer.Deserialize<List<WorkoutFixture>>(
                   json,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new InvalidOperationException(
                   "Engine 3.0 validation fixture could not be read.");
    }

    private sealed record WorkoutFixture(
        string WorkoutTypeCode,
        string DisplayName,
        string WorkoutCategoryCode,
        decimal DefaultMinutes,
        int DefaultRestSec,
        string? ProfileVersionCode,
        int? ProfileVersionNumber,
        string? ProfileJsonBase64);

    private sealed record PersonScenario(
        string Name,
        DateOnly DateOfBirth,
        string SexCode,
        decimal HeightIn,
        decimal WeightLb,
        decimal BodyFatFraction,
        IReadOnlyCollection<Engine30OccupationalRoleInput> Roles,
        string LifestyleActivityCode,
        string PlanCode);

    private sealed class FixtureProfileProvider(
        IReadOnlyList<WorkoutFixture> fixtures)
        : IEngine30WorkoutRuntimeProfileProvider
    {
        private readonly IReadOnlyDictionary<string, WorkoutFixture> _fixtures =
            fixtures.ToDictionary(
                x => x.WorkoutTypeCode,
                StringComparer.OrdinalIgnoreCase);

        private readonly Engine30WorkoutProfileService _baselineBuilder =
            new(null!);

        private static readonly JsonSerializerOptions ProfileJsonOptions =
            new(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };

        public Task<Engine30WorkoutRuntimeProfileResolution> ResolveAsync(
            string workoutTypeCode,
            CancellationToken cancellationToken = default)
        {
            if (!_fixtures.TryGetValue(workoutTypeCode, out var fixture))
            {
                throw new KeyNotFoundException(
                    $"Validation fixture does not contain workout '{workoutTypeCode}'.");
            }

            Engine30WorkoutAdminProfile profile;
            string sourceCode;

            if (!string.IsNullOrWhiteSpace(fixture.ProfileJsonBase64))
            {
                var json =
                    Encoding.Unicode.GetString(
                        Convert.FromBase64String(
                            fixture.ProfileJsonBase64));

                profile =
                    JsonSerializer.Deserialize<Engine30WorkoutAdminProfile>(
                        json,
                        ProfileJsonOptions)
                    ?? throw new InvalidOperationException(
                        $"Stored Admin profile for {fixture.WorkoutTypeCode} could not be deserialized.");

                sourceCode =
                    Engine30WorkoutRuntimeProfileSourceCodes.AdminActiveProfile;
            }
            else
            {
                var workout =
                    new WorkoutTypeDefinition
                    {
                        WorkoutTypeDefinitionId = Guid.NewGuid(),
                        ConfigVersionId = Guid.NewGuid(),
                        WorkoutTypeCode = fixture.WorkoutTypeCode,
                        DisplayName = fixture.DisplayName,
                        WorkoutCategoryCode = fixture.WorkoutCategoryCode,
                        WeeklyRoleCode = "OTHER_LOAD",
                        FuelPriorityCode = "AUTO",
                        SchedulePrincipleCode = "SCHEDULE_NORMALLY",
                        DefaultMinutes = fixture.DefaultMinutes,
                        DefaultRestSec = checked((short)fixture.DefaultRestSec),
                        IsSelectable = true,
                        IsActive = true
                    };

                profile =
                    _baselineBuilder.BuildBaselineProfile(
                        workout);

                sourceCode =
                    Engine30WorkoutRuntimeProfileSourceCodes.BaselineFallback;
            }

            Engine30WorkoutProfileService.Validate(profile);

            return Task.FromResult(
                new Engine30WorkoutRuntimeProfileResolution(
                    Profile: profile,
                    Provenance:
                        new Engine30WorkoutRuntimeProfileProvenance(
                            ConfigVersionCode: "CFG-3000.0",
                            WorkoutTypeDefinitionId: Guid.NewGuid(),
                            WorkoutTypeCode: fixture.WorkoutTypeCode,
                            WorkoutDisplayName: fixture.DisplayName,
                            SourceCode: sourceCode,
                            ProfileVersionId:
                                fixture.ProfileVersionNumber.HasValue
                                    ? Guid.NewGuid()
                                    : null,
                            ProfileVersionCode:
                                string.IsNullOrWhiteSpace(
                                    fixture.ProfileVersionCode)
                                    ? null
                                    : fixture.ProfileVersionCode,
                            ProfileVersionNumber:
                                fixture.ProfileVersionNumber,
                            IsBaseline:
                                !fixture.ProfileVersionNumber.HasValue
                                || fixture.ProfileVersionNumber.Value == 1,
                            ProfileCreatedAtUtc: null)));
        }
    }
}
