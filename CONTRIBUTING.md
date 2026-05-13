# 贡献指南

感谢您对 CaptureMouse 项目的关注！

## 开发环境

- Windows 11
- .NET 9 SDK
- Visual Studio 2022 或 VS Code

## 构建项目

```bash
# 还原依赖
dotnet restore src/CaptureMouse/CaptureMouse.csproj

# 构建
dotnet build src/CaptureMouse/CaptureMouse.csproj

# 运行测试
dotnet run --project src/CaptureMouse
```

## 提交规范

- 使用清晰的提交信息
- 每个迭代单独提交
- 重大变更请先开 Issue 讨论

## 迭代开发

项目按迭代方式开发，当前进度：

- ✅ 迭代一：基础连接层
- ⏳ 迭代二：输入捕获层
- ⏳ 迭代三：控制权切换
- ⏳ 迭代四：优化与完善
- ⏳ 迭代五：打包与部署

## 代码规范

- 遵循 C# 编码规范
- 添加必要的注释
- 保持代码简洁

## 许可证

MIT License
