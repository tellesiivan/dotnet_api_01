using System.Data;
using DotnetApi.Data;
using DotnetApi.Helpers;
using DotnetApi.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using DotnetApi.Models;
using AutoMapper;

namespace DotnetApi.Controllers;

// You need to be authorized to access one of the following endpoints unless adding [AllowAnonymous] on top of the endpoint(Login/Register)
[Authorize]
[ApiController]
[Route("[controller]")]
public class AuthController : ControllerBase
{
    private readonly DataContextDapper _dapper;
    private readonly AuthHelper _authHelper;
    private readonly ReusableSql _reusableSQL;
    private readonly IMapper _mapper;

    public AuthController(IConfiguration config)
    {
        _mapper = new Mapper(
            new MapperConfiguration(config =>
            {
                config.CreateMap<UserForRegistrationDto, UserComplete>();
            })
        );
        _reusableSQL = new(config);
        _dapper = new(config);
        _authHelper = new(config);
    }

    [AllowAnonymous]
    [HttpPost("Register")]
    public IActionResult Register(UserForRegistrationDto userForRegistration)
    {
        string Password = userForRegistration.Password;
        string ConfirmationalPassword = userForRegistration.PasswordConfirm;

        string sqlCheckIfUserExist =
            "SELECT Email FROM TutorialAppSchema.Auth WHERE Email = '"
            + userForRegistration.Email
            + "'";

        IEnumerable<string> existingUser = _dapper.LoadData<string>(sqlCheckIfUserExist);

        // Throw an Exception if password and confirmational password do not match
        if (!Password.Equals(ConfirmationalPassword))
        {
            throw new Exception("Passwords do not match");
        }
        // Throw an Exception if there is already user/s with that email
        else if (existingUser.Any())
        {
            return StatusCode(420, "User with this email already exists!");
        }

        UserForLoginDto userForLoginDto =
            new() { Email = userForRegistration.Email, Password = userForRegistration.Password };

        if (!_authHelper.SetPassword(userForLoginDto))
        {
            throw new Exception("Unable to register user at this time");
        }

        UserComplete userComplete = _mapper.Map<UserComplete>(userForRegistration);
        userComplete.Active = true;

        if (!_reusableSQL.UpsertUser(userComplete))
        {
            return StatusCode(423, "Failed to add user");
        }

        // successful registration
        return Ok();
    }

    [HttpPut("ResetPassword")]
    public IActionResult ResetPassword(UserForLoginDto userForLogin)
    {
        if (!_authHelper.SetPassword(userForLogin))
        {
            return StatusCode(412, "Unable to reset password");
        }

        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("Login")]
    public IActionResult Login(UserForLoginDto userForLogin)
    {
        string sqlForHashAndSalt =
            @"EXEC TutorialAppSchema.spLoginConfirmation_Get
                @Email = @EmailParam";

        DynamicParameters sqlParameters = new();
        sqlParameters.Add("@EmailParam", userForLogin.Email, DbType.String);

        UserForLoginConfirmationDto userForLoginConfirmation =
            _dapper.LoadSingleDataWithParameters<UserForLoginConfirmationDto>(
                sqlForHashAndSalt,
                sqlParameters
            );

        byte[] passwordHash = _authHelper.GetPasswordHash(
            userForLogin.Password,
            userForLoginConfirmation.PasswordSalt
        );

        for (int index = 0; index < passwordHash.Length; index++)
        {
            // cannot compare both password hashes (passwordHash == userForLoginConfirmation.PasswordHash) since they are objects, therefore we need to compare each byte in the array
            if (passwordHash[index] != userForLoginConfirmation.PasswordHash[index])
            {
                return StatusCode(401, "Password was incorrect!!");
            }
        }

        string userIdSql =
            @"
                SELECT UserId FROM TutorialAppSchema.Users WHERE Email = '"
            + userForLogin.Email
            + "'";

        int userId = _dapper.LoadSingleData<int>(userIdSql);

        string JwtToken = _authHelper.CreateToken(userId);

        // return a key value pair as the response
        Dictionary<string, string> response = new() { { "token", JwtToken } };

        return Ok(response);
    }

    [HttpGet("RefreshToken")]
    public string RefreshToken()
    {
        string sqlGetUserId =
            @"
                SELECT UserId FROM TutorialAppSchema.Users WHERE UserId = '"
            + User.FindFirst("userId")?.Value
            + "'";

        int userId = _dapper.LoadSingleData<int>(sqlGetUserId);

        return _authHelper.CreateToken(userId);
    }
}
