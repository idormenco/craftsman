﻿namespace Craftsman.Builders.Tests.FunctionalTests;

using System;
using System.IO;
using Craftsman.Services;
using Domain;
using Domain.Enums;
using Helpers;

public class AddListTestBuilder
{
    private readonly ICraftsmanUtilities _utilities;

    public AddListTestBuilder(ICraftsmanUtilities utilities)
    {
        _utilities = utilities;
    }

    public void CreateTests(string solutionDirectory, string testDirectory, Entity entity, Feature feature, string projectBaseName)
    {
        var classPath = ClassPathHelper.FunctionalTestClassPath(testDirectory, $"{feature.Name}Tests.cs", entity.Plural, projectBaseName);
        var fileText = WriteTestFileText(solutionDirectory, testDirectory, classPath, entity, feature.IsProtected, feature, projectBaseName);
        _utilities.CreateFile(classPath, fileText);
    }

    private static string WriteTestFileText(string solutionDirectory, string srcDirectory, ClassPath classPath, Entity entity, bool isProtected, Feature feature, string projectBaseName)
    {
        var dtoUtilClassPath = ClassPathHelper.DtoClassPath(srcDirectory, "", entity.Plural, projectBaseName);
        var testUtilClassPath = ClassPathHelper.FunctionalTestUtilitiesClassPath(srcDirectory, projectBaseName, "");
        var fakerClassPath = ClassPathHelper.TestFakesClassPath(srcDirectory, "", entity.Name, projectBaseName);
        var parentFakerClassPath = ClassPathHelper.TestFakesClassPath(srcDirectory, "", feature.ParentEntity, projectBaseName);
        var permissionsClassPath = ClassPathHelper.PolicyDomainClassPath(srcDirectory, "", projectBaseName);

        var permissionsUsing = isProtected
            ? $"{Environment.NewLine}using {permissionsClassPath.ClassNamespace};"
            : string.Empty;

        var authOnlyTests = isProtected ? $@"
            {CreateEntityTestUnauthorized(entity)}
            {CreateEntityTestForbidden(entity)}" : "";

        return @$"namespace {classPath.ClassNamespace};

using {dtoUtilClassPath.ClassNamespace};
using {fakerClassPath.ClassNamespace};
using {parentFakerClassPath.ClassNamespace};
using {testUtilClassPath.ClassNamespace};{permissionsUsing}
using System.Net;
using System.Threading.Tasks;

public class {Path.GetFileNameWithoutExtension(classPath.FullClassPath)} : TestBase
{{
    {CreateEntityTest(entity, feature, isProtected)}
    {NotFoundCreationTest(entity, feature, isProtected)}
    {InvalidCreationTest(entity, isProtected)}{authOnlyTests}
}}";
    }

    private static string CreateEntityTest(Entity entity, Feature feature, bool isProtected)
    {
        var createDto = FileNames.GetDtoName(entity.Name, Dto.Creation);
        var fakeEntityForCreation = $"Fake{createDto}";
        var fakeEntityVariableName = $"{entity.Name.LowercaseFirstLetter()}List";
        var fakeParentEntity = $"fake{feature.ParentEntity}";
        var fakeParentCreationDto = FileNames.FakerName(FileNames.GetDtoName(feature.ParentEntity, Dto.Creation));

        var testName = $"create_{entity.Name.ToLower()}_list_returns_created_using_valid_dto";
        testName += isProtected ? "_and_valid_auth_credentials" : "";
        var clientAuth = isProtected ? @$"

        var callingUser = await AddNewSuperAdmin();
        FactoryClient.AddAuth(callingUser.Identifier);" : "";

        return $@"
    [Fact]
    public async Task {testName}()
    {{
        // Arrange
        var {fakeParentEntity} = new Fake{feature.ParentEntity}Builder().Build();
        await InsertAsync({fakeParentEntity});
        var {fakeEntityVariableName} = new List<{createDto}> {{new {fakeEntityForCreation} {{ }}.Generate()}};{clientAuth}

        // Act
        var route = ApiRoutes.{entity.Plural}.CreateBatch;
        var result = await FactoryClient.PostJsonRequestAsync($""{{route}}?{feature.BatchPropertyName.ToLower()}={{{fakeParentEntity}.Id}}"", {fakeEntityVariableName});

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Created);
    }}";
    }

    private static string NotFoundCreationTest(Entity entity, Feature feature, bool isProtected)
    {
        var createDto = FileNames.GetDtoName(entity.Name, Dto.Creation);
        var fakeEntityForCreation = $"Fake{createDto}";
        var fakeEntityVariableName = $"{entity.Name.LowercaseFirstLetter()}List";

        var testName = $"create_{entity.Name.ToLower()}_list_returns_notfound_when_fk_doesnt_exist";
        testName += isProtected ? "_and_valid_auth_credentials" : "";
        var clientAuth = isProtected ? @$"

        FactoryClient.AddAuth(new[] {{Roles.SuperAdmin}});" : "";

        return $@"
    [Fact]
    public async Task {testName}()
    {{
        // Arrange
        var {fakeEntityVariableName} = new List<{createDto}> {{new {fakeEntityForCreation} {{ }}.Generate()}};{clientAuth}

        // Act
        var route = ApiRoutes.{entity.Plural}.CreateBatch;
        var result = await FactoryClient.PostJsonRequestAsync($""{{route}}?{feature.BatchPropertyName.ToLower()}={{Guid.NewGuid()}}"", {fakeEntityVariableName});

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }}";
    }

    private static string InvalidCreationTest(Entity entity, bool isProtected)
    {
        var createDto = FileNames.GetDtoName(entity.Name, Dto.Creation);
        var fakeEntityForCreation = $"Fake{createDto}";
        var fakeEntityVariableName = $"{entity.Name.LowercaseFirstLetter()}List";

        var testName = $"create_{entity.Name.ToLower()}_list_returns_4xx_when_no_fk_param";
        testName += isProtected ? "_and_valid_auth_credentials" : "";
        var clientAuth = isProtected ? @$"

        FactoryClient.AddAuth(new[] {{Roles.SuperAdmin}});" : "";

        return $@"
    [Fact]
    public async Task {testName}()
    {{
        // Arrange
        var {fakeEntityVariableName} = new List<{createDto}> {{new {fakeEntityForCreation} {{ }}.Generate()}};{clientAuth}

        // Act
        var result = await FactoryClient.PostJsonRequestAsync(ApiRoutes.{entity.Plural}.CreateBatch, {fakeEntityVariableName});

        // Assert
        result.StatusCode.Should()
            .BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }}";
    }

    private static string CreateEntityTestUnauthorized(Entity entity)
    {
        var fakeEntity = FileNames.FakerName(entity.Name);
        var fakeEntityVariableName = $"{entity.Name.LowercaseFirstLetter()}";

        return $@"
    [Fact]
    public async Task create_{entity.Name.ToLower()}_list_returns_unauthorized_without_valid_token()
    {{
        // Arrange
        var {fakeEntityVariableName} = new {fakeEntity} {{ }}.Generate();

        await InsertAsync({fakeEntityVariableName});

        // Act
        var route = ApiRoutes.{entity.Plural}.CreateBatch;
        var result = await FactoryClient.PostJsonRequestAsync(route, {fakeEntityVariableName});

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }}";
    }

    private static string CreateEntityTestForbidden(Entity entity)
    {
        var fakeEntity = FileNames.FakerName(entity.Name);
        var fakeEntityVariableName = $"{entity.Name.LowercaseFirstLetter()}";

        return $@"
    [Fact]
    public async Task create_{entity.Name.ToLower()}_list_returns_forbidden_without_proper_scope()
    {{
        // Arrange
        var {fakeEntityVariableName} = new {fakeEntity} {{ }}.Generate();
        FactoryClient.AddAuth();

        await InsertAsync({fakeEntityVariableName});

        // Act
        var route = ApiRoutes.{entity.Plural}.CreateBatch;
        var result = await FactoryClient.PostJsonRequestAsync(route, {fakeEntityVariableName});

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }}";
    }
}
