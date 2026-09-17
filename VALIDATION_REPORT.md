# v1001 package validation

Generated: 2026-08-12

## Reference arithmetic

Independent validation of TC-001:

- Protein: 190 g
- Planned Major Push glycogen demand: 85 g
- Base carbohydrate target: 190 g
- 4-day absolute restoration: 70.5%
- Functional target: 75%
- Target achievement: 93.9%
- Carryover allocation: 10 g
- Pre-workout allocation: 10 g
- Final carbohydrate target: 190 g
- Fat: 70 g
- Calories: 2150

Result: PASS.

## Configuration seed

- Plans: 5
- Workout types: 25
- Global settings: 33

Required schedule-first tables and deployment files are present.

## Compilation note

The generation environment does not contain the .NET SDK, so the C# project could not be compiled here. The supplied `04_deploy_application.ps1` deliberately runs `dotnet restore` and `dotnet build -c Release` before it creates or uploads the deployment ZIP. If compilation fails on your computer, the script stops before changing the Azure application.
