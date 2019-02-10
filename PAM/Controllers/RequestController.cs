﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PAM.Data;
using PAM.Extensions;
using PAM.Models;

namespace PAM.Controllers
{
    [Authorize]
    public class RequestController : Controller
    {
        private readonly RequestService _requestService;
        private readonly OrganizationService _orgService;

        public RequestController(RequestService requestService, OrganizationService orgService)
        {
            _requestService = requestService;
            _orgService = orgService;
        }

        [HttpGet]
        public IActionResult Self()
        {
            string username = ((ClaimsIdentity)User.Identity).GetClaim(ClaimTypes.NameIdentifier);
            ViewData["Requests"] = _requestService.GetRequestsByUsername(username);
            return View();
        }

        [HttpGet]
        public IActionResult ReviewRequests()
        {
            int supervisorId = int.Parse(((ClaimsIdentity)User.Identity).GetClaim("EmployeeId"));
            var requestsForReview = _requestService.GetRequestsForReview(supervisorId);

            ViewData["RequestsForReview"] = requestsForReview;

            return View();
        }

        [HttpGet]
        public IActionResult RequestForReview(int reqId)
        {
            var request = _requestService.GetRequestedSystemsByRequestId(reqId);
            TempData["RequestId"] = reqId;
            ViewData["RequestForReview"] = request;
            var requestedSystems = request.Systems;
            ViewData["RelatedSystems"] = requestedSystems;
            //List<RequestedSystem> tempReqSys = (List<RequestedSystem>)requestedSystems;
            //ViewData["Unit"] = _orgService.GetUnitSystemBySystemId(tempSystem.)

            return View();
        }

        [HttpPost]
        public IActionResult RequestForReview(string submitValue, string comment)
        {
            var request = _requestService.GetRequestedSystemsByRequestId((int)TempData["RequestId"]);
            var review = _requestService.GetReviewByRequestId(request.RequestId);
            
            switch (submitValue)
            {
                case "approve":
                    request.RequestStatus = RequestStatus.Approved;
                    _requestService.UpdateRequest(request);
                    review.Approve();
                    _requestService.UpdateReview(review);
                    SendReviewResultEmail(request.RequestedFor.Email);
                    break;
                case "reject":
                    request.RequestStatus = RequestStatus.Denied;
                    _requestService.UpdateRequest(request);
                    review.Deny(comment);
                    SendReviewResultEmail(request.RequestedFor.Email);
                    break;
            }

            return RedirectToAction("ReviewRequests");
        }

        [HttpGet]
        public IActionResult ProcessRequests(){
            //-----TODO-----
            ViewData["Requests"] = _requestService.GetRequests();
            return View();
        }

        [HttpGet]
        public IActionResult AllRequests(){
            ViewData["Requests"] = _requestService.GetRequests();
            return View();
        }

        [HttpGet]
        public IActionResult Delete(int? id)
        {
            if (id == null) return NotFound();

            string username = ((ClaimsIdentity)User.Identity).GetClaim(ClaimTypes.NameIdentifier);
            var requests = _requestService.GetRequestsByUsername(username);
            if (requests == null) return RedirectToAction("Self");
            else return View();
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirm(int id)
        {
            string username = ((ClaimsIdentity)User.Identity).GetClaim(ClaimTypes.NameIdentifier);
            var requests = _requestService.GetRequestsByUsername(username);

            foreach(var request in requests)
            {
                if (request.RequestId == id) _requestService.RemoveRequest(request);
            }
            return RedirectToAction("Self");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public void SendReviewResultEmail(string requesterEmail)
        {
            string receipient = requesterEmail;
            string emailName = "ReviewResult";

            var model = new { _emailHelper.AppUrl, _emailHelper.AppEmail, Request };

            string subject = _emailHelper.GetSubjectFromTemplate(emailName, model, _email.Renderer);
            _email.To(receipient)
                .Subject(subject)
                .UsingTemplateFromFile(_emailHelper.GetBodyTemplateFile(emailName), model)
                .SendAsync();

            ViewData["Receipient"] = receipient;
            ViewData["Subject"] = subject;

            return RedirectToAction("Self", "Request");
        }
    }
}
