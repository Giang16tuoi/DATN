﻿using APPDATA.Models;
using APPVIEW.Services;
using APPVIEW.ViewModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using _APPAPI.Service;
using AspNetCoreHero.ToastNotification.Abstractions;
using APPDATA.DB;
using System.Text.Encodings.Web;

using Microsoft.EntityFrameworkCore;

using Microsoft.AspNetCore.Rewrite;
using System.Xml.Linq;
using X.PagedList;

namespace APPVIEW.Controllers
{
    public class AccountController : Controller
    {
        private readonly HttpClient _httpClient;
        private Getapi<Account> getapi;
        private Getapi<Role> _getapiRole;
        private Getapi<Address> getapiAddress;
        private readonly ISendEmail _sendEmail;
        private readonly ShoppingDB _context;

        public AccountController(HttpClient httpClient, INotyfService notyf, ISendEmail sendEmail)
        {
            getapi = new Getapi<Account>();
            _httpClient = httpClient;
            _getapiRole = new Getapi<Role>();
            getapiAddress = new Getapi<Address>();
            _sendEmail = sendEmail;
            _context = new ShoppingDB();
        }
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetList(int?page)
        {
            ViewBag.Roles = GetListRole();
            var obj = getapi.GetApi("Account");
            int pageSize = 8;
            int pageNumber = (page ?? 1);
            return View(obj.OrderByDescending(x => x.Id).ToPagedList(pageNumber, pageSize));
        }
        public async Task<IActionResult> Search(string searchTerm)
        {
            var lstAcc = getapi.GetApi("Voucher").ToList();

            var searchResult = lstAcc
                .Where(v =>
                    v.Email.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

            if (searchResult.Any())
            {
                return View("GetList", searchResult);
            }

            return NotFound("Voucher không tồn tại");
        }

        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        // POST: SupplierController1/Create
        [HttpPost, AllowAnonymous]

        public async Task<IActionResult> Register(RegisterVm obj, Account item)
        {
            if (string.IsNullOrEmpty(obj.Email) || string.IsNullOrEmpty(obj.Name) || string.IsNullOrEmpty(obj.ConfirmPassword))
            {

                ViewData["ErrorMessage"] = "Please enter your information.";
                return View("Register", obj);

            }
            if (obj.Password != obj.ConfirmPassword)
            {
                ViewData["ErrorMessage"] = "Password and confirm password not correct,try again.";
                return View("Register", obj);
            }
            var md5pass = MD5Pass.GetMd5Hash(obj.Password);

            obj.Password = md5pass;
            var jsonData = JsonConvert.SerializeObject(obj);

            var address = new Address()
            {
                id = Guid.NewGuid(),
                AccountId = obj.Id,
                City = "N/A",
                Ward = "N/A",
                District = "N/A",
                PhoneNumber = obj.PhoneNumber,
                Description = "N/A",
                Name = obj.Name,
                Province = "N/A",
                DefaultAddress = "N/A",
                SpecificAddress = "N/A"


            };
            var jsonDataAddress = JsonConvert.SerializeObject(address);

            var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            var contentAddress = new StringContent(jsonDataAddress, Encoding.UTF8, "application/json");


            var responese = await _httpClient.PostAsync("https://localhost:7042/api/Account/Register", content);
            var responeseAdress = await _httpClient.PostAsync("https://localhost:7042/api/Address/Post", contentAddress);
            if (responese.IsSuccessStatusCode && responeseAdress.IsSuccessStatusCode)
            {

                return Redirect("~/Account/Login");

            }
            else
            {
                var errorResponse = await responese.Content.ReadAsStringAsync();
                return View();
            }
        }
        [HttpGet, AllowAnonymous]

        public IActionResult Login()
        {
            return View();
        }
        [HttpPost, AllowAnonymous]

        public async Task<IActionResult> Login(LoginVm obj)
        {
            if (string.IsNullOrWhiteSpace(obj.Email) || string.IsNullOrWhiteSpace(obj.Password))
            {
                ViewData["ErrorMessage"] = "Please enter your email and password.";
                return View("Login", obj);
            }

            var md5pass = MD5Pass.GetMd5Hash(obj.Password);
            obj.Password = md5pass;
            var content = new StringContent(JsonConvert.SerializeObject(obj), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://localhost:7042/api/Account/Login", content);

            if (response.IsSuccessStatusCode)
            {

                var responseData = await response.Content.ReadAsStringAsync();
                var loginResult = JsonConvert.DeserializeObject<TokenVm>(responseData);
                TokenVm tokenV = new TokenVm { AccessToken = loginResult.AccessToken };
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(loginResult.AccessToken);

                bool checkRoleAdmin = false;
                var checkRoles = jwt.Claims.FirstOrDefault(c => c.Type == "role")?.Value;
                if (checkRoles.StartsWith("Adm"))
                {
                    checkRoleAdmin = true;
                }
                else
                {
                    checkRoleAdmin = false;
                }

                //Check Role and Get information of user
                var claims = new List<Claim>
                   {
                    new Claim(ClaimTypes.Email, obj.Email),

                    };
                // Trích xuất thông tin quyền từ mã thông báo JWT
                var roles = jwt.Claims.ToList();

                //    Thêm các quyền từ mã thông báo JWT vào danh tính của người dùng
                if (roles.Any())
                {
                    foreach (var role in roles)
                    {

                        if (role.Type.ToString() == "role")
                        {
                            claims.Add(new Claim(ClaimTypes.Role, role.Value));

                        }
                        if (role.Type.ToString() == "Id")
                        {
                            var acc = getapi.GetApi("Account").FirstOrDefault(c => c.Id.ToString() == role.Value);
                            SessionService.SetObjToJson(HttpContext.Session, "Account", acc);
                        }

                    }

                }
                var Avatar = jwt.Claims.FirstOrDefault(c => c.Type == "Avatar")?.Value;
                var Name = jwt.Claims.FirstOrDefault(c => c.Type == "Name")?.Value;
                var Id_User = jwt.Claims.FirstOrDefault(c => c.Type == "Id")?.Value;
                if (Id_User != null)
                {
                    claims.Add(new Claim("Id", Id_User.ToString()));
                    claims.Add(new Claim("Avatar", Avatar.ToString()));
                    claims.Add(new Claim("Name", Name));
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                Response.Cookies.Append("AccessToken", loginResult.AccessToken);
                if (checkRoleAdmin == true)
                {
                    return Redirect("~/Admin/Admin/Index");

                }
                else
                {
                    return Redirect("~/Home/Index");
                }



            }

            ViewData["ErrorMessage"] = "Email or password wrong.";
            return View("Login", obj);

        }
        public async Task<IActionResult> Edit(Guid id)
        {
            ViewBag.Roles = GetListRole();
            var lst = getapi.GetApi("Account");
            return View(lst.Find(c => c.Id == id));
        }


        [HttpPost, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(Account obj, [Bind] IFormFile imageFile)
        {
            try
            {

                obj.Avatar = AddImg(imageFile);
                await getapi.UpdateObj(obj, "Account");
                return RedirectToAction("GetList");
            }
            catch
            {
                return View();
            }
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await getapi.DeleteObj(id, "Account");
            return RedirectToAction("GetList");

        }
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {

            Response.Cookies.Delete("AccessToken");

            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);


            return Redirect("~/Account/Login");
        }

        [HttpGet, AllowAnonymous]
        public async Task<IActionResult> MyProfile(Guid id_User)
        {

            var obj = getapi.GetApi("Account");
            var address = getapiAddress.GetApi("Address");

            var user = obj.FirstOrDefault(c => c.Id == id_User);
            var addressOfUser = address.FirstOrDefault(c => c.AccountId == id_User);

            if (addressOfUser != null || user != null)
            {
                return View(new AccountVm()
                {
                    Id = addressOfUser.id,
                    Avatar = user.Avatar,
                    Name = user.Name,
                    Email = user.Email,
                    Password = user.Password,
                    AccountId = id_User,
                    SpecificAddress = addressOfUser.SpecificAddress,
                    Ward = addressOfUser.Ward,
                    City = addressOfUser.City,
                    District = addressOfUser.District,
                    PhoneNumber = addressOfUser.PhoneNumber,
                    DefaultAddress = addressOfUser.DefaultAddress,
                    Province = addressOfUser.Province,
                    Description = addressOfUser.Description,
                    Id_Role = user.IdRole,


                });
            }
            return View();

        }
        public string AddImg(IFormFile imageFile)
        {


            if (imageFile != null && imageFile.Length > 0) // Không null và không trống
            {
                //Trỏ tới thư mục wwwroot để lát nữa thực hiện việc Copy sang
                var path = Path.Combine(
                    Directory.GetCurrentDirectory(), "wwwroot", "UserImage", imageFile.FileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    // Thực hiện copy ảnh vừa chọn sang thư mục mới (wwwroot)
                    imageFile.CopyTo(stream);
                }
                // Gán lại giá trị cho Description của đối tượng bằng tên file ảnh đã được sao chép

            }
            return imageFile.FileName;


        }
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> MyProfile(AccountVm obj, [Bind] IFormFile imageFile)
        {
            try
            {

                var user = new Account();
                
                   
           

                if (imageFile != null)
                {
                    user.Id = obj.AccountId;
                    user.Email = obj.Email;
                    user.Name = obj.Name;
                    user.Password = obj.Password;
                    user.IdRole = obj.Id_Role;
                    user.Avatar = AddImg(imageFile);
                }
                

                var address = new Address()
                {
                    AccountId = obj.AccountId,
                    Province = obj.Province,
                    DefaultAddress = obj.DefaultAddress,
                    SpecificAddress = obj.SpecificAddress,
                    City = obj.City,
                    District = obj.District,
                    PhoneNumber = obj.PhoneNumber,
                    Ward = obj.Ward,
                    Name = obj.Name,
                    id = obj.Id,
                    Description = obj.Description

                };

                var responeseAcc = await getapi.UpdateObj(user, "Account");
                var responeseAdd = await getapiAddress.UpdateObj(address, "Address");

                return Redirect("~/Home/Index");
            }
            catch
            {
                return View();
            }
        }
        public List<Role> GetListRole()
        {
            var obj = _getapiRole.GetApi("Role");
            return obj;
        }
        public IActionResult ForgotPassword()
        {
            return View();
        }
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> ForgotPassword(ResetPass resetPass)
        {
            if (string.IsNullOrWhiteSpace(resetPass.Email))
            {
                ViewData["ErrorMessage"] = "Email is required!";
                return View(resetPass);

            }
            var user = _context.Accounts.FirstOrDefault(u => u.Email == resetPass.Email);

            if (user != null)
            {
                var resetToken = RundomCodeService.GenerateRandomCode(6);
                user.ResetPasswordcode = resetToken;
                await _context.SaveChangesAsync();

                // Send reset link  email
                var resetLink = Url.Action("ResetPass", "Account", new { }, Request.Scheme);
                var emailSubject = "Password Reset Request";
                var emailBody = $"Hi {user.Name}, <br/> You recently requested to reset your password for your account. This is your request code: <b>{resetToken}</b> Please click on the following link to reset your password: <a href='{HtmlEncoder.Default.Encode(resetLink)}'>Reset Password</a>";

                await _sendEmail.SendEmailAsync(user.Email, emailSubject, emailBody);

                ViewData["Sucsess"] = "Reset password code has been sent to your email , Check your email now.";
                return View(resetPass);


            }
            else
            {
                ViewData["ErrorMessage"] = "Email not found!";
                return View(resetPass);
            }

        }
        public IActionResult ResetPass()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> ResetPass(ResetPasswordVm obj)
        {
            if (string.IsNullOrWhiteSpace(obj.ConfirmCode) || string.IsNullOrWhiteSpace(obj.ConfirmPassword) || string.IsNullOrWhiteSpace(obj.NewPassword))
            {
                ViewData["ErrorMessage"] = "Please enter your Confirm code and New password!";
                return View(obj);

            }
            if (obj.NewPassword != obj.ConfirmPassword)
            {
                ViewData["ErrorMessage"] = "Your new password or confirm password are wrong, try again!";
                return View(obj);
            }
            else
            {
                var user = _context.Accounts.FirstOrDefault(s => s.Email == obj.Email);
                if (user != null)
                {
                    if (obj.ConfirmCode != user.ResetPasswordcode)
                    {
                        ViewData["ErrorMessage"] = " Confirm code is wrong,try again!";
                        return View(obj);
                    }
                    else
                    {
                        user.Password = MD5Pass.GetMd5Hash(obj.NewPassword);
                        user.ResetPasswordcode = null;
                        await _context.SaveChangesAsync();
                        ViewData["Sucsess"] = " your password is changed,return to the login page!";
                        return View(obj);
                    }

                }
                else
                {
                    ViewData["ErrorMessage"] = "Email not found!";
                    return View(obj);
                }

            }

        }
        public IActionResult ChangePassword()
        {

            return View();
        }
        [HttpPost, AllowAnonymous]
        public async Task<IActionResult> ChangePassword(ChangePasswordVm obj)
        {
            if (ModelState.IsValid)
            {
                var IdUser = ((System.Security.Claims.ClaimsIdentity)User.Identity).FindFirst("Id");
                string Id_userValue = IdUser?.Value;
                obj.IdUser = Guid.Parse(Id_userValue);

                var user = await _context.Accounts.FirstOrDefaultAsync(c => c.Id == obj.IdUser);
                if (user != null)
                {
                    if (MD5Pass.GetMd5Hash(obj.OldPassword) != user.Password)
                    {
                        ViewData["ErrorMessage"] = "Old Password is incorrect,try agin!";
                        return View("ChangePassword", obj);
                    }
                    if (obj.NewPassWord!=obj.ConfirmPassword)
                    {
                        ViewData["ErrorMessage"] = " New password is incorrect,try again!";
                        return View("ChangePassword", obj);
                    }

                    user.Password = MD5Pass.GetMd5Hash(obj.NewPassWord);
                    _context.Update(user);
                    await _context.SaveChangesAsync();
                    ViewData["Sucsess"] = "Changed password successfully";
                    return Redirect("~/Home/Index");


                }
            }
            return View();
        }

    }
}
