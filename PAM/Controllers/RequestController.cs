﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using AutoMapper;
using FluentEmail.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PAM.Data;
using PAM.Extensions;
using PAM.Models;
using PAM.Services;

namespace PAM.Controllers
{
    [Authorize]
    public class RequestController : Controller
    {
        private readonly IADService _adService;
        private readonly UserService _userService;
        private readonly RequestService _requestService;
        private readonly IFluentEmail _email;
        private readonly EmailHelper _emailHelper;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;

        public RequestController(IADService adService, UserService userService, RequestService requestService,
            SystemService systemService, IFluentEmail email, EmailHelper emailHelper, IMapper mapper, ILogger<AccountController> logger)
        {
            _adService = adService;
            _userService = userService;
            _requestService = requestService;
            _email = email;
            _emailHelper = emailHelper;
            _mapper = mapper;
            _logger = logger;
        }

        [HttpGet]
        public ActionResult CreateRequest()
        {
            return View(_requestService.GetRequestTypes());
        }

        [HttpPost]
        public IActionResult CreateRequest(string forSelf, string requestedForUsername, int requestTypeId, Requester requester)
        {
            Employee employee = new Employee((ClaimsIdentity)User.Identity);
            Requester requestedBy = _mapper.Map<Requester>(employee);
            requestedBy = _userService.CreateRequester(requestedBy);

            Requester requestedFor;
            if (forSelf.Equals("yes"))
            {
                requestedFor = requestedBy;
            }
            else if (requestedForUsername != null)
            {
                employee = _adService.GetEmployeeByUsername(requestedForUsername);
                employee = _userService.HasEmployee(requestedForUsername) ?
                    _userService.UpdateEmployee(employee) : _userService.CreateEmployee(employee);
                requestedFor = _mapper.Map<Requester>(employee);
                requestedFor = _userService.CreateRequester(requestedFor);
            }
            else
            {
                // If RequestedFor doesn't have an AD account, we need their username, email, etc.
                // to create a PAM Employee record for them. This function is not implemented yet.
                throw new NotImplementedException();
            }

            Request request = new Request();
            request.RequestedBy = requestedBy;
            request.RequestedFor = requestedFor;
            request.RequestTypeId = requestTypeId;

            request.Reviews = new List<Review>();
            var requestType = _requestService.GetRequestType(requestTypeId);
            var requiredSignatures = requestType.RequiredSignatures.OrderBy(s => s.Order).ToList();
            for (int i = 0; i < requiredSignatures.Count; ++i)
            {
                var review = new Review
                {
                    ReviewOrder = i,
                    ReviewerTitle = requiredSignatures[i].Title
                };
                if (review.ReviewerTitle.Equals("Supervisor"))
                {
                    Employee supervisor = _adService.GetEmployeeByName(request.RequestedFor.SupervisorName);
                    supervisor = _userService.HasEmployee(supervisor.Username) ?
                        _userService.UpdateEmployee(supervisor) : _userService.CreateEmployee(supervisor);
                    review.ReviewerId = supervisor.EmployeeId;
                }
                request.Reviews.Add(review);
            }

            request = _requestService.CreateRequest(request);

            _logger.LogInformation($"User {User.Identity.Name} created request {request.RequestId}.");

            return RedirectToAction("RequesterInfo", new { id = request.RequestId });

        }

        [HttpGet]
        public IActionResult RequesterInfo(int id)
        {
            var request = _requestService.GetRequest(id);
            ViewData["request"] = request;
            return View(request.RequestedFor);
        }

        [HttpPost]
        public IActionResult RequesterInfo(int id, Requester requester, bool saveDraft = false)
        {
            _userService.UpdateRequester(requester);
            var request = _requestService.GetRequest(id);
            if (saveDraft) RedirectToAction("MyRequests", "Request");
            else{
                switch (request.RequestTypeId)
                {
                    case 11:
                        return RedirectToAction("UnitSelection", "EditAddAccess", new { id = request.RequestId });
                    default:
                        return RedirectToAction("UnitSelection", "EditPortfolioRequest", new { id = request.RequestId });
                }
            }
            return View();
        }

        public IActionResult ViewRequest(int id)
        {
            var request = _requestService.GetRequest(id);
            return View(request);
        }

        public IActionResult EditRequest(int id)
        {
            var request = _requestService.GetRequest(id);
            switch (request.RequestTypeId)
            {
                default:
                    return RedirectToAction("RequesterInfo", "EditPortfolioRequest", new { id });
            }
        }

        public IActionResult SubmitRequest(int id)
        {
            var request = _requestService.GetRequest(id);
            if (request.RequestStatus != RequestStatus.Draft)
                throw new InvalidOperationException();

            request.RequestStatus = RequestStatus.UnderReview;
            request.SubmittedOn = DateTime.Now;
            _requestService.SaveChanges();

            Employee reviewer = request.OrderedReviews[0].Reviewer;
            string receipient = reviewer.Email;
            string emailName = "ReviewRequest";
            var model = new { _emailHelper.AppUrl, _emailHelper.AppEmail, Request = request };
            string subject = _emailHelper.GetSubjectFromTemplate(emailName, model, _email.Renderer);
            _email.To(receipient)
                .Subject(subject)
                .UsingTemplateFromFile(_emailHelper.GetBodyTemplateFile(emailName), model)
                .SendAsync();

            return RedirectToAction("MyRequests");
        }

        public IActionResult DeleteRequest(int id)
        {
            var request = _requestService.GetRequest(id);
            if (request.RequestStatus != RequestStatus.Draft)
                throw new InvalidOperationException();

            request.Deleted = true;
            _requestService.SaveChanges();
            return RedirectToAction("MyRequests");
        }

        [HttpGet]
        public IActionResult MyRequests()
        {
            string username = ((ClaimsIdentity)User.Identity).GetClaim(ClaimTypes.NameIdentifier);
            var allRequests = _requestService.GetRequestsByUsername(username);
            List<Request> underReviewRequests = new List<Request>();
            List<Request> completedRequests = new List<Request>();
            List<Request> draftRequests = new List<Request>();
            foreach (var request in allRequests)
            {
                if (request.RequestStatus == RequestStatus.Draft)
                    draftRequests.Add(request);
                else if (request.RequestStatus == RequestStatus.UnderReview)
                    underReviewRequests.Add(request);
                else
                    completedRequests.Add(request);
            }
            ViewData["allRequests"] = allRequests;
            ViewData["underReviewRequests"] = underReviewRequests;
            ViewData["completedRequests"] = completedRequests;
            ViewData["draftRequests"] = draftRequests;
            return View();
        }

        [HttpGet]
        public IActionResult AllRequests()
        {
            string username = ((ClaimsIdentity)User.Identity).GetClaim(ClaimTypes.NameIdentifier);
            var allRequests = _requestService.GetRequests();
            List<Request> underReviewRequests = new List<Request>();
            List<Request> completedRequests = new List<Request>();
            List<Request> draftRequests = new List<Request>();
            foreach (var request in allRequests)
            {
                if (request.RequestStatus == RequestStatus.Draft)
                    draftRequests.Add(request);
                else if (request.RequestStatus == RequestStatus.UnderReview)
                    underReviewRequests.Add(request);
                else
                    completedRequests.Add(request);
            }
            ViewData["allRequests"] = allRequests;
            ViewData["underReviewRequests"] = underReviewRequests;
            ViewData["completedRequests"] = completedRequests;
            ViewData["draftRequests"] = draftRequests;
            return View();
        }

        [HttpGet]
        public IActionResult ReviewRequests()
        {
            //-----TODO-----
            ViewData["Requests"] = _requestService.GetRequests();
            return View();
        }
    }
}
